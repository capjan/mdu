using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mdu.Performance;

// Compiled only into the generated diagnostic source tree, never the shipped CLI.
public static class Probe
{
    private static readonly AsyncLocal<Scope?> Current = new();
    private static readonly Dictionary<string, Stat> Stats = new();
    private static readonly object Gate = new();
    private static readonly string Output = Environment.GetEnvironmentVariable("MDU_PERF_OUTPUT")!;
    private static Timer? _timer;
    private static int _sequence;

    public static void Initialize()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Snapshot("exit");
        // The harness requests collections only while idle, after latency measurement.
        _timer = new Timer(_ =>
        {
            var request = Output + ".collect";
            if (!File.Exists(request)) return;
            File.Delete(request);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Snapshot("idle-post-gc");
        }, null, 100, 100);
    }

    public static Scope Begin(string name) => new(name);

    public static void Snapshot(string reason)
    {
        lock (Gate)
        {
            var gc = GC.GetGCMemoryInfo();
            var record = new ProbeSnapshot
            {
                Reason = reason, Sequence = _sequence++,
                AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true),
                ManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
                GcHeapBytes = gc.HeapSizeBytes, GcFragmentedBytes = gc.FragmentedBytes,
                Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
                WorkingSetBytes = Environment.WorkingSet,
                Stages = Stats.ToDictionary(p => p.Key, p => new StageSnapshot(p.Value.Calls,
                    p.Value.Ticks * 1000d / Stopwatch.Frequency,
                    p.Value.ExclusiveTicks * 1000d / Stopwatch.Frequency, p.Value.Bytes))
            };
            File.AppendAllText(Output, JsonSerializer.Serialize(record, ProbeJsonContext.Default.ProbeSnapshot) + "\n");
            Stats.Clear();
        }
    }

    public sealed class Scope : IDisposable
    {
        private readonly string _name, _path;
        private readonly bool _processAllocation;
        private readonly Scope? _parent;
        private readonly long _start, _bytes;
        private long _childTicks;

        internal Scope(string name)
        {
            _name = name;
            _parent = Current.Value;
            _path = _parent is null ? name : _parent._path + "/" + name;
            Current.Value = this;
            _processAllocation = name is "render" or "serve.edit" or "serve.rebuild" or "page" or "html" or "html.write" or "openapi.parse";
            _bytes = Allocated();
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var ticks = Stopwatch.GetTimestamp() - _start;
            var bytes = Allocated() - _bytes;
            Current.Value = _parent;
            if (_parent is not null)
            {
                _parent._childTicks += ticks;
            }
            lock (Gate)
            {
                if (!Stats.TryGetValue(_path, out var stat)) Stats[_path] = stat = new Stat();
                stat.Calls++;
                stat.Ticks += ticks;
                stat.ExclusiveTicks += ticks - _childTicks;
                stat.Bytes += bytes;
            }
            if (_name == "serve.edit") Snapshot(_name);
        }

        private long Allocated() => _processAllocation
            ? GC.GetTotalAllocatedBytes(precise: true)
            : GC.GetAllocatedBytesForCurrentThread();
    }

    private sealed class Stat
    {
        public long Calls, Ticks, ExclusiveTicks, Bytes;
    }
}

internal sealed class ProbeSnapshot
{
    public string Reason { get; init; } = "";
    public int Sequence { get; init; }
    public long AllocatedBytes { get; init; }
    public long ManagedBytes { get; init; }
    public long GcHeapBytes { get; init; }
    public long GcFragmentedBytes { get; init; }
    public int Gen0 { get; init; }
    public int Gen1 { get; init; }
    public int Gen2 { get; init; }
    public long WorkingSetBytes { get; init; }
    public Dictionary<string, StageSnapshot> Stages { get; init; } = new();
}

internal sealed record StageSnapshot(long Calls, double InclusiveMs, double ExclusiveMs, long AllocatedBytes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProbeSnapshot))]
internal partial class ProbeJsonContext : JsonSerializerContext;
