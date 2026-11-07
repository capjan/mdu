using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Mdu.Performance;

internal static class Measurements
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };

    internal static async Task<JsonObject> Render(Options options, string name, string dll, string sample, bool diagnostic)
    {
        var output = Path.Combine(options.Work, "output", name);
        Clean(output);
        var probe = diagnostic ? Log(options, $"{options.Mode}-{name}-render-{sample}.jsonl") : null;
        await using var child = new ChildProcess(dll, ["render", Path.Combine(options.Work, "corpora", name), output],
            Log(options, $"{options.Mode}-{name}-render-{sample}.log"), probe);
        var result = await child.Finish();
        result["workload"] = name;
        result["command"] = "render";
        result["sample"] = sample;
        if (name.StartsWith("diagram-", StringComparison.Ordinal))
        {
            Require(File.ReadAllText(Path.Combine(output, "index.html")).Contains("<svg", StringComparison.Ordinal), "Diagram was not drawn.");
            result["validation"] = new JsonObject { ["svg"] = true };
        }
        else result["validation"] = Validate(output);
        return result;
    }

    // The synthetic corpora use the compact form, the documentation the nested one.
    internal static string ChangeSiteName(string configuration, string name) =>
        Regex.Replace(configuration, "^(site\\.name:|  name:).*$", "$1 " + name, RegexOptions.Multiline);

    internal static async Task<JsonObject> Serve(Options options, string name, string dll, int sample)
    {
        var root = Path.Combine(options.Work, "corpora", name);
        var output = Path.Combine(options.Work, "output", name);
        Clean(output);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = $"http://127.0.0.1:{port}/";
        var originals = new[] { "README.md", "mdu.yml", "diagram.md", "api.yaml" }
            .ToDictionary(file => Path.Combine(root, file), file => File.ReadAllText(Path.Combine(root, file)));
        await using var browser = options.Mode is "browser" or "browser-check" ? await Browser.Start() : null;
        var probe = options.Diagnostic ? Log(options, $"{options.Mode}-{name}-serve-{sample}.jsonl") : null;
        await using var child = new ChildProcess(dll, ["serve", root, output, "--port", port.ToString(), "--no-browser"],
            Log(options, $"{options.Mode}-{name}-serve-{sample}.log"), probe);
        using var cancellation = new CancellationTokenSource();
        var events = Channel.CreateUnbounded<(long Time, JsonObject Message)>();
        var edits = new JsonArray();
        var memory = new JsonArray();
        try
        {
            await child.WaitLine("Serving ");
            Require((await Fetch(url)).Contains("baseline-content-sentinel", StringComparison.Ordinal), "Missing initial page content.");
            var startup = Stopwatch.GetElapsedTime(child.Started).TotalMilliseconds;
            var completeStartup = WaitCompleteSite(url, child.Started);
            double? browserStartup = null;
            if (browser is not null)
            {
                await browser.Navigate(url);
                await browser.WaitDisplayed("baseline-content-sentinel");
                browserStartup = Stopwatch.GetElapsedTime(child.Started).TotalMilliseconds;
                var state = await browser.Inspect();
                Require(state["iframes"]!.GetValue<int>() == 0 && state["localScripts"]!.GetValue<bool>(), "The initial page must use only local scripts and no eager video player.");
            }
            await child.WaitLine("Site updated.");
            var completeSiteStartup = await completeStartup;
            Validate(output);
            if (options.Mode == "browser-check")
                await browser!.VerifyInteractions(url, Log(options, $"{name}-browser-check.png"));
            using var streamClient = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await streamClient.GetAsync(url + "__mdu/events", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
            var readerTask = ReadEvents(stream, events.Writer, cancellation.Token);
            await NextEvent(events.Reader);
            if (options.Memory) memory.Add(await Collect(probe!, 0));
            var editKinds = options.EditKinds ?? ["content", "config", "diagram", "spec"];
            var kinds = options.Memory
                ? Enumerable.Repeat("content", options.BodyEdits).Concat(Enumerable.Range(0, options.MixedRounds).SelectMany(_ => new[] { "config", "diagram", "spec" })).ToArray()
                : Enumerable.Range(0, options.Rounds).SelectMany(_ => editKinds).ToArray();
            var currentMarkers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["index.html"] = "baseline-content-sentinel", ["diagram.html"] = "Diagram fixture", ["measure.html"] = "Measurement fixture"
            };
            for (var number = 1; number <= kinds.Length; number++)
            {
                var kind = kinds[number - 1];
                var marker = $"perfchange{number:0000}{kind}";
                var (file, target) = kind switch
                {
                    "content" => ("README.md", "index.html"),
                    "config" => ("mdu.yml", "measure.html"),
                    "diagram" => ("diagram.md", "diagram.html"),
                    _ => ("api.yaml", "measure.html")
                };
                var path = Path.Combine(root, file);
                var text = kind switch
                {
                    "content" => originals[path].Replace("baseline-content-sentinel", marker, StringComparison.Ordinal),
                    "config" => ChangeSiteName(originals[path], marker),
                    "diagram" => originals[path].Replace("Load", marker, StringComparison.Ordinal),
                    _ => originals[path].Replace("Baseline operation 0", marker, StringComparison.Ordinal)
                };
                var before = HtmlTimes(output);
                if (browser is not null)
                {
                    await browser.Navigate(url + target);
                    await browser.WaitDisplayed(currentMarkers[target]);
                }
                // Repeated OS notifications can leave another completion event from the previous save.
                // Only notifications observed after this edit belong to its measurement interval.
                while (events.Reader.TryRead(out _)) { }
                var started = Stopwatch.GetTimestamp();
                await File.WriteAllTextAsync(path, text);
                async Task<long> DisplayTime()
                {
                    await browser!.WaitDisplayed(marker);
                    return Stopwatch.GetTimestamp();
                }
                var display = browser is null ? null : DisplayTime();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                while (!(await Fetch(url + target)).Contains(marker, StringComparison.Ordinal)) await Task.Delay(10, timeout.Token);
                var visible = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var completed = await NextEvent(events.Reader);
                while (completed.Time < started) completed = await NextEvent(events.Reader);
                double? displayed = null;
                if (display is not null)
                {
                    displayed = Stopwatch.GetElapsedTime(started, await display).TotalMilliseconds;
                }
                currentMarkers[target] = marker;
                var message = completed.Message;
                Require(message["reloadAll"]!.GetValue<bool>() || message["paths"]!.AsArray().Any(p => p!.GetValue<string>() == "/" + target), "Reload event did not include the changed page.");
                Require((await Fetch(url + target)).Contains(marker, StringComparison.Ordinal), "Reload event published stale content.");
                if (kind is "content" or "spec")
                    Require(File.ReadAllText(Path.Combine(output, "search-index.json")).Contains(marker, StringComparison.Ordinal), $"Search index is stale after {name} edit {number} ({kind}).");
                var reloadMs = Stopwatch.GetElapsedTime(started, completed.Time).TotalMilliseconds;
                var completeMs = reloadMs;
                if (JsonNode.Parse(await Fetch(url + "__mdu/status"))?["building"]?.GetValue<bool>() == true)
                    completeMs = await WaitCompleteSite(url, started);
                var changed = HtmlTimes(output).Where(p => !before.TryGetValue(p.Key, out var value) || value != p.Value).Select(p => p.Key).ToArray();
                Require(changed.Contains(target, StringComparer.Ordinal), "Changed page was not rewritten.");
                edits.Add(new JsonObject
                {
                    ["edit"] = number, ["kind"] = kind, ["visibleMs"] = visible,
                    ["browserDisplayMs"] = displayed,
                    ["reloadMs"] = reloadMs, ["completeMs"] = completeMs,
                    ["rewrittenHtml"] = changed.Length
                });
                if (number % 100 == 0)
                    Console.WriteLine($"{name} server {sample}: {number}/{kinds.Length} edits verified");
                if (options.Memory && (number % 10 == 0 || number == kinds.Length)) memory.Add(await Collect(probe!, number));
                await Task.Delay(100);
            }
            var validation = Validate(output);
            var usage = await child.Finish(stop: true);
            cancellation.Cancel();
            // Stopping the server may end the event stream before cancellation is observed.
            try { await readerTask; } catch (Exception e) when (e is OperationCanceledException or HttpIOException) { }
            usage["workload"] = name;
            usage["command"] = "serve";
            usage["sample"] = sample;
            usage["startupMs"] = startup;
            usage["completeSiteStartupMs"] = completeSiteStartup;
            usage["browserStartupMs"] = browserStartup;
            usage["browser"] = browser?.Version.DeepClone();
            if (options.Mode == "browser-check") usage["browserChecks"] = new JsonObject
            {
                ["consentAndRevocation"] = true, ["searchSection"] = true, ["keyboardFocus"] = true, ["anchorsAndNavigation"] = true
            };
            usage["edits"] = edits;
            usage["memory"] = memory;
            usage["validation"] = validation;
            await File.WriteAllTextAsync(Log(options, $"{options.Mode}-{name}-serve-{sample}.result.json"), usage.ToJsonString(Program.Json));
            return usage;
        }
        finally
        {
            cancellation.Cancel();
            await child.DisposeAsync();
            foreach (var (path, text) in originals) await File.WriteAllTextAsync(path, text);
        }
    }

    private static async Task ReadEvents(Stream stream, ChannelWriter<(long, JsonObject)> writer, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellation) is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                await writer.WriteAsync((Stopwatch.GetTimestamp(), JsonNode.Parse(line[6..])!.AsObject()), cancellation);
    }

    private static async Task<double> WaitCompleteSite(string url, long started)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (JsonNode.Parse(await Fetch(url + "__mdu/status"))?["building"]?.GetValue<bool>() == true)
            await Task.Delay(10, timeout.Token);
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static async Task<(long Time, JsonObject Message)> NextEvent(ChannelReader<(long Time, JsonObject Message)> reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        return await reader.ReadAsync(timeout.Token);
    }

    private static async Task<JsonObject> Collect(string probe, int edit)
    {
        var count = File.ReadLines(probe).Count();
        await File.WriteAllTextAsync(probe + ".collect", "");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            foreach (var line in File.ReadLines(probe).Skip(count))
            {
                var record = JsonNode.Parse(line)!.AsObject();
                if (record["reason"]!.GetValue<string>() != "idle-post-gc") continue;
                record["edit"] = edit;
                return record;
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    internal static async Task<JsonObject> Compare(Options options, string name)
    {
        var normal = options.Cli ?? Path.Combine(options.Repo, "Source/Mdu/bin/Release/net10.0/mdu.dll");
        var diagnostic = Path.Combine(options.Work, "diagnostic-source/Source/Mdu/bin/Release/net10.0/mdu.dll");
        Dictionary<string, string>? previous = null;
        foreach (var dll in new[] { normal, diagnostic })
        {
            await Render(options, name, dll, dll == diagnostic ? "diagnostic" : "normal", dll == diagnostic);
            var output = Path.Combine(options.Work, "output", name);
            var hashes = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(output, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            if (previous is not null) Require(previous.Count == hashes.Count && previous.All(p => hashes.GetValueOrDefault(p.Key) == p.Value), "Diagnostic output differs: " + name);
            previous = hashes;
        }
        return new JsonObject { ["workload"] = name, ["files"] = previous!.Count, ["identical"] = true };
    }

    internal static JsonObject Validate(string output)
    {
        var html = File.ReadAllText(Path.Combine(output, "measure.html"));
        var diagram = File.ReadAllText(Path.Combine(output, "diagram.html"));
        foreach (var value in new[] { "id=\"stable-anchor\"", "href=\"index.html\"", "href=\"diagram.html\"", "sidebar", "breadcrumb", "openapi", "/items/0", "class=\"keyword\"", "<svg", "data-embed-url=", "data-media-consent=" })
            Require(html.Contains(value, StringComparison.Ordinal), "Missing expected output: " + value);
        Require(diagram.Contains("<svg", StringComparison.Ordinal) && diagram.Contains("Service 0", StringComparison.Ordinal), "Missing rendered diagram.");
        Require(!Regex.IsMatch(html, "<iframe\\b"), "Video must require consent.");
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "search-index.json")))!.AsArray();
        var matches = index.Where(entry => entry!["text"]!.GetValue<string>().Contains("baseline-search-sentinel", StringComparison.Ordinal)).ToArray();
        Require(matches.Length == 1 && matches[0]!["url"]!.GetValue<string>() == "measure.html#stable-anchor", "Search section differs.");
        Require(!Directory.EnumerateFiles(output, "*.pdf", SearchOption.AllDirectories).Any(), "Unexpected PDF workload.");
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        return new JsonObject { ["htmlPages"] = files.Count(f => f.EndsWith(".html", StringComparison.Ordinal)), ["searchEntries"] = index.Count, ["outputBytes"] = files.Sum(f => new FileInfo(f).Length) };
    }

    private static Dictionary<string, long> HtmlTimes(string output) => Directory.GetFiles(output, "*.html", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(output, p).Replace(Path.DirectorySeparatorChar, '/'), p => File.GetLastWriteTimeUtc(p).Ticks);

    private static async Task<string> Fetch(string url)
    {
        try { return await Http.GetStringAsync(url); }
        catch (HttpRequestException) { return ""; }
        catch (TaskCanceledException) { return ""; }
    }

    private static string Log(Options options, string name) => Path.Combine(options.Work, "logs", name);
    private static void Clean(string path) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
