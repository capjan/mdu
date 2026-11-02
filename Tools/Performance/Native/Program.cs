using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--meter") return await ProcessMeter.Run(args);
            var options = Options.Read(args);
            Directory.CreateDirectory(options.Work);
            Directory.CreateDirectory(Path.Combine(options.Work, "logs"));
            if (options.Mode == "inspect")
            {
                using var input = File.OpenRead(options.Archive ?? throw new ArgumentException("inspect requires --archive <file>"));
                using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
                Console.WriteLine(JsonNode.Parse(gzip)!.ToJsonString(Json));
                return 0;
            }
            if (options.Mode == "generate")
            {
                Console.WriteLine(Corpora.Generate(options).ToJsonString(Json));
                return 0;
            }
            if (options.Mode is "summary" or "archive")
            {
                Report.Print(options.Work);
                if (options.Archive is not null) Report.Archive(options.Work, options.Archive);
                return 0;
            }
            var dll = Path.Combine(options.Repo, "Source/Mdu/bin/Release/net10.0/mdu.dll");
            if (options.Mode is "diagnostic" or "memory")
                dll = await Instrumentation.Build(options);
            var results = new JsonObject
            {
                ["environment"] = await EnvironmentInfo(options),
                ["formatVersion"] = 2,
                ["corpora"] = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(options.Work, "corpora.json"))),
                ["runs"] = new JsonArray()
            };
            var runs = results["runs"]!.AsArray();
            if (options.Mode == "startup")
            {
                for (var sample = 0; sample <= options.Samples; sample++)
                {
                    await using var child = new ChildProcess(dll, ["--help"], Path.Combine(options.Work, "logs", $"startup-{sample}.log"), null);
                    var measurement = await child.Finish();
                    if (sample > 0) runs.Add(measurement);
                }
                Save(options, results);
                return 0;
            }
            var names = options.Mode == "complexity" ? new[] { "diagram-6", "diagram-18", "diagram-36" } : options.Workloads;
            foreach (var name in names)
            {
                if (options.Mode == "compare")
                {
                    runs.Add(await Measurements.Compare(options, name));
                    continue;
                }
                await Measurements.Render(options, name, dll, "warm", diagnostic: options.Diagnostic);
                for (var sample = 0; sample < options.Samples; sample++)
                {
                    var run = await Measurements.Render(options, name, dll, sample.ToString(), options.Diagnostic);
                    runs.Add(run);
                    Save(options, results);
                    Console.WriteLine($"{name} render {sample}: {run["wallMs"]!.GetValue<double>():F1} ms");
                }
                if (options.Mode == "complexity") continue;
                for (var sample = 0; sample < (options.Diagnostic ? 1 : options.Servers); sample++)
                {
                    var run = await Measurements.Serve(options, name, dll, sample);
                    runs.Add(run);
                    Save(options, results);
                    Console.WriteLine($"{name} serve {sample}: {run["startupMs"]!.GetValue<double>():F1} ms");
                }
            }
            Save(options, results);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Save(Options options, JsonObject result) =>
        File.WriteAllText(Path.Combine(options.Work, options.Mode + ".json"), result.ToJsonString(Json));

    private static async Task<JsonObject> EnvironmentInfo(Options options) => new()
    {
        ["commit"] = (await Command("git", ["-C", options.Repo, "rev-parse", "HEAD"])).Trim(),
        ["platform"] = RuntimeInformation.OSDescription,
        ["machine"] = RuntimeInformation.ProcessArchitecture.ToString(),
        ["dotnet"] = await Command("dotnet", ["--info"]),
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["browserDisplayObservation"] = "expected DOM text retained across two animation frames; not physical panel presentation",
        ["runtimeSettings"] = JsonSerializer.SerializeToNode(Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key.ToString()!.StartsWith("DOTNET_") || entry.Key.ToString()!.StartsWith("COMPlus_"))
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString())),
        ["invocation"] = JsonSerializer.SerializeToNode(Environment.GetCommandLineArgs()),
        ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["peakRssMethod"] = "OS getrusage(RUSAGE_CHILDREN) from a fresh .NET helper; helper startup/memory excluded"
    };

    internal static async Task<string> Command(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException((await output) + await error);
        return await output;
    }
}

internal sealed record Options(string Repo, string Work, string Mode, int Samples, int Servers, int Rounds, string[] Workloads, string? Archive)
{
    public bool Diagnostic => Mode is "diagnostic" or "memory";

    public static Options Read(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--")) throw new ArgumentException($"Expected an option: {key}");
            var items = new List<string>();
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) items.Add(args[++i]);
            values.Add(key, items);
        }
        string Value(string key, string fallback) => values.TryGetValue(key, out var list) ? list.Single() : fallback;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Mdu.sln"))) root = root.Parent;
        return new Options(Canonical(Value("--repo", root?.FullName ?? Directory.GetCurrentDirectory())),
            Canonical(Value("--work", Path.Combine(Path.GetTempPath(), "mdu-performance-188"))),
            Value("--mode", "baseline"), int.Parse(Value("--samples", "7")), int.Parse(Value("--servers", "3")),
            int.Parse(Value("--rounds", "3")), values.TryGetValue("--workloads", out var names) ? names.ToArray() : ["small", "docs", "large"],
            values.TryGetValue("--archive", out var archive) ? Path.GetFullPath(archive.Single()) : null);
    }

    private static string Canonical(string path)
    {
        path = Path.GetFullPath(path);
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null)
                current = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true)!.FullName;
        }
        return current;
    }
}
