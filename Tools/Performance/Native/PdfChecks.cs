using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

internal static class PdfChecks
{
    internal static async Task<JsonObject> Run(Options options, string cli)
    {
        var root = Path.Combine(options.Work, "pdf-corpus");
        var output = Path.Combine(options.Work, "pdf-output");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "README.md");
        const string initial = "# PDF preview\n\npdf-initial-sentinel\n";
        const string link = "\n[PDF](mdu:pdf)\n";
        File.WriteAllText(home, initial);
        for (var page = 0; page < 180; page++)
            File.WriteAllText(Path.Combine(root, $"page-{page:000}.md"), $"# Page {page}\n\n" +
                string.Concat(Enumerable.Repeat("Paragraph for PDF pagination.\n\n", 10)) +
                "```csharp\n" + string.Join('\n', Enumerable.Range(0, 80).Select(line => $"var value{line} = {line};")) + "\n```\n");
        using var inputHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(root).Order(StringComparer.Ordinal))
        {
            inputHash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\0"));
            inputHash.AppendData(File.ReadAllBytes(path));
        }
        var fingerprint = Convert.ToHexStringLower(inputHash.GetHashAndReset());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = $"http://127.0.0.1:{port}/";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var browser = await Browser.Start();
        await using var child = new ChildProcess(cli, ["serve", root, output, "--port", port.ToString(), "--no-browser"],
            Path.Combine(options.Work, "logs/pdf-check.log"), null);
        async Task WaitFor(Func<Task<bool>> check)
        {
            while (!await check()) await Task.Delay(10, deadline.Token);
        }
        async Task<string> Status() => await client.GetStringAsync(url + "__mdu/status", deadline.Token);
        await child.WaitLine("Serving ");
        Measurements.Require((await client.GetStringAsync(url, deadline.Token)).Contains("pdf-initial-sentinel", StringComparison.Ordinal), "Missing initial PDF preview.");
        var firstHttp = Stopwatch.GetElapsedTime(child.Started).TotalMilliseconds;
        await WaitFor(async () => !(await Status()).Contains("\"building\":true", StringComparison.Ordinal));
        await browser.Navigate(url);
        await File.WriteAllTextAsync(home, initial + link, deadline.Token);
        await browser.WaitDisplayed("Updating PDF downloads");
        await browser.CheckPdfMessage(false, Path.Combine(options.Work, "logs/pdf-pending.png"));
        using (var pending = await client.GetAsync(url + "documentation.pdf", deadline.Token))
            Measurements.Require(pending.StatusCode == HttpStatusCode.ServiceUnavailable, "Pending PDF was advertised as ready.");

        await File.WriteAllTextAsync(home, "# PDF preview\n\n```openapi\nmissing.json\n```" + link, deadline.Token);
        await browser.WaitDisplayed("PDF downloads unavailable");
        using (var failed = await client.GetAsync(url + "documentation.pdf", deadline.Token))
            Measurements.Require(failed.StatusCode == HttpStatusCode.ServiceUnavailable, "Failed PDF was advertised as ready.");
        var started = Stopwatch.GetTimestamp();
        await File.WriteAllTextAsync(home, "# PDF preview\n\npdf-latest-sentinel\n" + link, deadline.Token);
        await browser.WaitDisplayed("pdf-latest-sentinel");
        var display = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        await WaitFor(async () => (await Status()).Contains("\"downloads\":\"ready\"", StringComparison.Ordinal));
        var complete = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        await browser.CheckPdfMessage(true, Path.Combine(options.Work, "logs/pdf-ready.png"));
        var pdf = Encoding.Latin1.GetString(await client.GetByteArrayAsync(url + "documentation.pdf", deadline.Token));
        Measurements.Require(pdf.StartsWith("%PDF-", StringComparison.Ordinal) &&
                             pdf.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes("pdf-latest-sentinel")), StringComparison.Ordinal) &&
                             !pdf.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes("pdf-initial-sentinel")), StringComparison.Ordinal), "PDF contains an obsolete generation.");
        var usage = await child.Finish(stop: true);
        usage["command"] = "pdf-check";
        usage["inputPages"] = 181;
        usage["inputSha256"] = fingerprint;
        usage["browser"] = browser.Version.DeepClone();
        usage["firstHttpMs"] = firstHttp;
        usage["editBrowserMs"] = display;
        usage["pdfCompleteMs"] = complete;
        usage["pendingFailedReadyAndLatestContent"] = true;
        return usage;
    }
}
