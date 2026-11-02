using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

internal static class Report
{
    internal static void Print(string work)
    {
        foreach (var file in new[] { "baseline.json", "browser.json" })
        {
        if (!File.Exists(Path.Combine(work, file))) continue;
        Console.WriteLine(file);
        var normal = JsonNode.Parse(File.ReadAllText(Path.Combine(work, file)))!;
        foreach (var group in normal["runs"]!.AsArray().GroupBy(r => r!["workload"]!.GetValue<string>()))
        {
            Console.WriteLine(group.Key);
            var renders = group.Where(r => r!["command"]!.GetValue<string>() == "render").ToArray();
            var servers = group.Where(r => r!["command"]!.GetValue<string>() == "serve").ToArray();
            if (renders.Length > 0)
            {
                Console.WriteLine("  render ms: " + Spread(renders.Select(r => r!["wallMs"]!.GetValue<double>())));
                Console.WriteLine("  peak RSS MiB: " + Spread(renders.Select(r => r!["peakRssMiB"]!.GetValue<double>())));
            }
            if (servers.Length == 0) continue;
            Console.WriteLine("  startup ms: " + Spread(servers.Select(r => r!["startupMs"]!.GetValue<double>())));
            foreach (var edits in servers.SelectMany(r => r!["edits"]!.AsArray()).GroupBy(e => e!["kind"]!.GetValue<string>()))
            {
                Console.WriteLine($"  {edits.Key} complete ms: " + Spread(edits.Select(e => e!["completeMs"]!.GetValue<double>())));
                Console.WriteLine($"  {edits.Key} HTTP ms: " + Spread(edits.Select(e => e!["visibleMs"]!.GetValue<double>())));
                var displayed = edits.Where(e => e!["browserDisplayMs"] is not null).Select(e => e!["browserDisplayMs"]!.GetValue<double>()).ToArray();
                if (displayed.Length > 0) Console.WriteLine($"  {edits.Key} browser ms: " + Spread(displayed));
            }
        }
        }
        foreach (var mode in new[] { "diagnostic", "memory" })
        {
            var path = Path.Combine(work, mode + ".json");
            if (!File.Exists(path)) continue;
            var result = JsonNode.Parse(File.ReadAllText(path))!;
            foreach (var group in result["runs"]!.AsArray().Where(r => r!["command"]!.GetValue<string>() == "render").GroupBy(r => r!["workload"]!.GetValue<string>()))
            {
                var profiles = group.Select(run => Path.Combine(work, "logs", $"{mode}-{group.Key}-render-{run!["sample"]}.jsonl"))
                    .Select(file => JsonNode.Parse(File.ReadLines(file).Last())!).ToArray();
                Console.WriteLine($"  {mode} {group.Key} allocated MiB: " + Spread(profiles.Select(p => p["allocatedBytes"]!.GetValue<double>() / 1048576)));
                if (mode != "diagnostic") continue;
                var stages = profiles.SelectMany(profile => profile["stages"]!.AsObject()).GroupBy(stage => stage.Key.Split('/')[^1]);
                foreach (var stage in stages.OrderByDescending(g => g.Sum(p => p.Value!["exclusiveMs"]!.GetValue<double>())).Take(30))
                    Console.WriteLine($"    {stage.Key}: aggregate exclusive {stage.Sum(p => p.Value!["exclusiveMs"]!.GetValue<double>()) / profiles.Length:F2} ms; calls {stage.Sum(p => p.Value!["calls"]!.GetValue<double>()) / profiles.Length:F0}");
            }
            foreach (var server in result["runs"]!.AsArray().Where(r => r!["command"]!.GetValue<string>() == "serve"))
                foreach (var point in server!["memory"]!.AsArray())
                    Console.WriteLine($"  {server["workload"]} post-GC edit {point!["edit"]}: {point["managedBytes"]!.GetValue<double>() / 1048576:F2} MiB");
        }
    }

    internal static string Spread(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var count = sorted.Length;
        var median = (sorted[(count - 1) / 2] + sorted[count / 2]) / 2;
        var text = $"{median:F1} ({sorted[0]:F1}–{sorted[^1]:F1}; n={count})";
        return count >= 300 ? text + $"; p95={sorted[(int)Math.Ceiling(count * .95) - 1]:F1}" : text;
    }

    internal static void Archive(string work, string output)
    {
        var data = new JsonObject();
        foreach (var file in Directory.EnumerateFiles(work, "*.json").Order(StringComparer.Ordinal))
            data[Path.GetFileName(file)] = JsonNode.Parse(File.ReadAllText(file));
        var profiles = new JsonObject();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(work, "logs"), "*.jsonl").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if ((!name.StartsWith("diagnostic-", StringComparison.Ordinal) && !name.StartsWith("memory-", StringComparison.Ordinal)) || name.Contains("-warm", StringComparison.Ordinal)) continue;
            profiles[name] = new JsonArray(File.ReadLines(file).Select(line => JsonNode.Parse(line)).ToArray());
        }
        data["profiles"] = profiles;
        using var stream = File.Create(output);
        using var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);
        using var writer = new System.Text.Json.Utf8JsonWriter(gzip);
        data.WriteTo(writer);
    }
}
