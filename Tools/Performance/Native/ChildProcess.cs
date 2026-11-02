using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

internal sealed class ChildProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly Task _stdout, _stderr;
    private readonly List<string> _lines = [];
    private readonly string _resultFile;
    private bool _finished;
    private bool _disposed;
    public long Started { get; private set; } = Stopwatch.GetTimestamp();

    public ChildProcess(string dll, IEnumerable<string> arguments, string log, string? probe)
    {
        _log = new StreamWriter(log);
        _resultFile = log + ".resources.json";
        File.Delete(_resultFile);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--meter");
        start.ArgumentList.Add(_resultFile);
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (probe is not null)
        {
            File.Delete(probe);
            start.Environment["MDU_PERF_OUTPUT"] = probe;
        }
        else start.Environment.Remove("MDU_PERF_OUTPUT");
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start mdu.");
        _stdout = Drain(_process.StandardOutput);
        _stderr = Drain(_process.StandardError);
    }

    private async Task Drain(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_lines)
            {
                if (line.StartsWith(ProcessMeter.StartPrefix, StringComparison.Ordinal))
                {
                    Started = long.Parse(line[ProcessMeter.StartPrefix.Length..]);
                    continue;
                }
                _lines.Add(line);
                _log.WriteLine(line);
                _log.Flush();
            }
        }
    }

    public async Task WaitLine(string prefix)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (true)
        {
            lock (_lines)
                if (_lines.Any(line => line.StartsWith(prefix, StringComparison.Ordinal))) return;
            if (_process.HasExited) throw new InvalidOperationException("mdu stopped before " + prefix);
            await Task.Delay(5, timeout.Token);
        }
    }

    public async Task<JsonObject> Finish(bool stop = false)
    {
        if (stop && !_process.HasExited)
        {
            if (OperatingSystem.IsWindows()) _process.Kill(entireProcessTree: true);
            else if (Kill(_process.Id, 2) != 0) throw new InvalidOperationException("Could not interrupt the preview process.");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(_stdout, _stderr);
        _finished = true;
        if (_process.ExitCode != 0) throw new InvalidOperationException($"mdu exited with {_process.ExitCode}: {string.Join('\n', _lines.TakeLast(20))}");
        return JsonNode.Parse(await File.ReadAllTextAsync(_resultFile))!.AsObject();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_finished)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            await Task.WhenAll(_stdout, _stderr);
        }
        await _log.DisposeAsync();
        _process.Dispose();
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
