using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

// A fresh .NET helper isolates RUSAGE_CHILDREN to this one CLI invocation.
// Its own CLR startup, memory and shutdown are outside the reported CLI measurements.
internal static class ProcessMeter
{
    internal const string StartPrefix = "__mdu_meter_start__ ";

    internal static async Task<int> Run(string[] args)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Exact process resource measurements currently support macOS and Linux.");
        var resultFile = args[1];
        var start = new ProcessStartInfo("dotnet");
        foreach (var argument in args.Skip(2)) start.ArgumentList.Add(argument);
        var started = Stopwatch.GetTimestamp();
        Console.WriteLine(StartPrefix + started);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start measured CLI.");
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            Kill(child.Id, 2);
        });
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Kill(child.Id, 15);
        });
        await child.WaitForExitAsync();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (GetResourceUsage(-1, out var usage) != 0) throw new InvalidOperationException("getrusage failed.");
        var data = new JsonObject
        {
            ["wallMs"] = elapsed,
            ["peakRssMiB"] = usage.MaxRss / (OperatingSystem.IsMacOS() ? 1048576d : 1024d),
            ["cpuMs"] = (usage.UserSeconds + usage.SystemSeconds) * 1000d + (usage.UserMicroseconds + usage.SystemMicroseconds) / 1000d
        };
        await File.WriteAllTextAsync(resultFile, data.ToJsonString());
        return child.ExitCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Usage
    {
        public long UserSeconds, UserMicroseconds, SystemSeconds, SystemMicroseconds;
        public long MaxRss, SharedRss, UnsharedData, UnsharedStack, MinorFaults, MajorFaults;
        public long Swaps, InputBlocks, OutputBlocks, MessagesSent, MessagesReceived, Signals, Voluntary, Involuntary;
    }

    [DllImport("libc", EntryPoint = "getrusage", SetLastError = true)]
    private static extern int GetResourceUsage(int who, out Usage usage);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
