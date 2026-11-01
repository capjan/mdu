using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Mdu.Test;

public class ServeCommandIntegrationTest
{
    [Fact]
    public async Task UpdatesScopedPdfsAndRemovesDownloadsWhenLinksDisappear()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-serve-pdf-{Guid.NewGuid():N}");
        var docs = Path.Combine(root, "Docs");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(Path.Combine(docs, "Topic"));
        var home = Path.Combine(docs, "README.md");
        var detail = Path.Combine(docs, "Topic", "Details.md");
        File.WriteAllText(home, "# Home\n\n[Topic](mdu:pdf?root=Topic/README.md)");
        File.WriteAllText(Path.Combine(docs, "Topic", "README.md"), "# Topic");
        File.WriteAllText(detail, "# Details\n\nBefore");
        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var baseUrl = $"http://127.0.0.1:{port}/";
        try
        {
            await WaitUntilAsync(async () => await PageContainsAsync(client, baseUrl, "_mdu-pdf/"), process);
            var html = await client.GetStringAsync(baseUrl);
            var pdfUrl = System.Text.RegularExpressions.Regex.Match(html,
                "href=\"(?<url>_mdu-pdf/[a-f0-9]+\\.pdf)\"").Groups["url"].Value;
            Assert.NotEmpty(pdfUrl);
            using (var response = await client.GetAsync(baseUrl + pdfUrl))
            {
                Assert.True(response.IsSuccessStatusCode);
                Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
                var pdf = System.Text.Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync());
                Assert.Contains("4265666F7265", pdf);
                Assert.DoesNotContain("486F6D65", pdf);
            }

            File.WriteAllText(detail, "# Details\n\nAfter");
            await WaitUntilAsync(async () =>
            {
                var pdf = System.Text.Encoding.Latin1.GetString(await client.GetByteArrayAsync(baseUrl + pdfUrl));
                return pdf.Contains("4166746572", StringComparison.Ordinal) && !pdf.Contains("4265666F7265", StringComparison.Ordinal);
            }, process);

            File.WriteAllText(Path.Combine(docs, "Topic", "New.md"), "# NewlyAdded");
            await WaitUntilAsync(async () =>
            {
                var pdf = System.Text.Encoding.Latin1.GetString(await client.GetByteArrayAsync(baseUrl + pdfUrl));
                return pdf.Contains("4E65776C794164646564", StringComparison.Ordinal);
            }, process);
            File.Delete(Path.Combine(docs, "Topic", "New.md"));
            await WaitUntilAsync(async () =>
            {
                var pdf = System.Text.Encoding.Latin1.GetString(await client.GetByteArrayAsync(baseUrl + pdfUrl));
                return !pdf.Contains("4E65776C794164646564", StringComparison.Ordinal);
            }, process);

            File.WriteAllText(home, "# Home\n\nDownload removed");
            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync(baseUrl + pdfUrl);
                return response.StatusCode == HttpStatusCode.NotFound;
            }, process);
            Assert.False(File.Exists(Path.Combine(site, "documentation.pdf")));
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KeepsAliasPageWhenItsFolderContainsOnlyAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Home\n\n[Guide](Guide.md)");
        File.WriteAllText(Path.Combine(docs, "Guide.md"), "# Guide\n");

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var aliasUrl = $"http://127.0.0.1:{port}/Guide/index.html";

        try
        {
            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync(aliasUrl);
                return response.IsSuccessStatusCode;
            }, process);

            Directory.CreateDirectory(Path.Combine(docs, "Guide"));
            File.WriteAllText(Path.Combine(docs, "Guide", "asset.svg"), "<svg />");
            File.WriteAllText(Path.Combine(docs, "Another.md"), "# Another\n");

            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/Another.html");
                return response.IsSuccessStatusCode &&
                       (await response.Content.ReadAsStringAsync()).Contains("Another", StringComparison.Ordinal);
            }, process);

            using var aliasResponse = await client.GetAsync(aliasUrl);
            Assert.True(aliasResponse.IsSuccessStatusCode);
            Assert.Contains("Guide.html", await aliasResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdatesMarkdownAssetsAndRemovesGeneratedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var site = Path.Combine(docs, "_site");
        Directory.CreateDirectory(docs);
        var markdownPath = Path.Combine(docs, "README.md");
        var assetPath = Path.Combine(docs, "diagram.svg");
        File.WriteAllText(markdownPath, "# Before\n\n![Diagram](diagram.svg)");
        File.WriteAllText(assetPath, "asset-v1");

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageUrl = $"http://127.0.0.1:{port}/index.html";
        var assetUrl = $"http://127.0.0.1:{port}/diagram.svg";

        try
        {
            await WaitUntilAsync(async () =>
            {
                var response = await client.GetAsync(pageUrl);
                if (!response.IsSuccessStatusCode) return false;
                return (await response.Content.ReadAsStringAsync()).Contains("Before", StringComparison.Ordinal);
            }, process);

            Assert.Equal("asset-v1", await client.GetStringAsync(assetUrl));

            File.WriteAllText(markdownPath, "# After\n\n![Diagram](diagram.svg)");
            File.WriteAllText(assetPath, "asset-v2");

            await WaitUntilAsync(async () =>
            {
                var response = await client.GetAsync(pageUrl);
                if (!response.IsSuccessStatusCode) return false;
                return (await response.Content.ReadAsStringAsync()).Contains("After", StringComparison.Ordinal);
            }, process);
            await WaitUntilAsync(async () => await client.GetStringAsync(assetUrl) == "asset-v2", process);

            File.Delete(markdownPath);
            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync(pageUrl);
                return response.StatusCode == HttpStatusCode.NotFound;
            }, process);
            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync(assetUrl);
                return response.StatusCode == HttpStatusCode.NotFound;
            }, process);
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdatesPagesSavedThroughTemporaryFilesAndFindsNewPages()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        var markdownPath = Path.Combine(docs, "README.md");
        File.WriteAllText(markdownPath, "# Home\n\nBefore");

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageUrl = $"http://127.0.0.1:{port}/index.html";

        try
        {
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "Before"), process);

            // Saves the way JetBrains IDEs do with "safe write": a temporary file replaces the page.
            File.WriteAllText(markdownPath + "___jb_tmp___", "# Home\n\nAfter");
            File.Move(markdownPath, markdownPath + "___jb_old___");
            File.Move(markdownPath + "___jb_tmp___", markdownPath);
            File.Delete(markdownPath + "___jb_old___");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "After"), process);

            Directory.CreateDirectory(Path.Combine(docs, "Guide"));
            File.WriteAllText(Path.Combine(docs, "Guide", "README.md"), "# Guide\n\nNew page");
            await WaitUntilAsync(async () =>
                await PageContainsAsync(client, $"http://127.0.0.1:{port}/Guide/index.html", "New page") &&
                await PageContainsAsync(client, pageUrl, "Guide/index.html"), process);
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportsRepositoryWhenAnImportIsAddedToAnExistingPage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var site = Path.Combine(root, "site");
        var remotes = Path.Combine(root, "remotes");
        var sourceRepository = Path.Combine(remotes, "acme", "handbook.git");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(Path.Combine(sourceRepository, "docs"));
        File.WriteAllText(Path.Combine(sourceRepository, "docs", "README.md"),
            $"# Handbook\n\nImported handbook content\n\n<!-- {Guid.NewGuid():N} -->\n");
        RunGit(sourceRepository, ["init", "-b", "main"]);
        RunGit(sourceRepository, ["config", "user.email", "mdu-tests@example.com"]);
        RunGit(sourceRepository, ["config", "user.name", "MDU tests"]);
        RunGit(sourceRepository, ["add", "."]);
        RunGit(sourceRepository, ["commit", "-m", "test source"]);
        var commit = RunGit(sourceRepository, ["rev-parse", "HEAD"]).Trim();

        File.WriteAllText(Path.Combine(docs, "README.md"), "# Home\n");
        var markerPath = Path.Combine(docs, "Handbook.md");
        const string frontMatter = "---\ntitle: Handbook\n";
        File.WriteAllText(markerPath, frontMatter + "---\n");

        // Git redirects the HTTPS source to the local repository, so the import needs no network access.
        var environment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = $"url.{new Uri(remotes).AbsoluteUri.TrimEnd('/')}/.insteadOf",
            ["GIT_CONFIG_VALUE_0"] = "https://git.example.test/"
        };
        var importCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mdu", "GitHubImports");

        var (process, port) = await StartServerOnFreePortAsync(docs, site, environment);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageUrl = $"http://127.0.0.1:{port}/Handbook/index.html";

        try
        {
            await WaitUntilAsync(async () =>
                await PageContainsAsync(client, $"http://127.0.0.1:{port}/Handbook.html", "Handbook"), process);

            File.WriteAllText(markerPath, frontMatter +
                "mdu:\n  import:\n    source: https://git.example.test/acme/handbook/-/tree/main/docs\n---\n");

            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "Imported handbook content"), process);
            Assert.Contains("href=\"https://git.example.test/acme/handbook\"", await client.GetStringAsync(pageUrl));
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
            // Only clones of the unique test commit are removed, so caches of other mdu processes stay untouched.
            if (Directory.Exists(importCache))
            {
                foreach (var entry in Directory.GetDirectories(importCache))
                {
                    if (TryRunGit(entry, ["rev-parse", "HEAD"])?.Trim() == commit)
                        Directory.Delete(entry, recursive: true);
                }
            }
        }
    }

    [Fact]
    public async Task WatchesOpenApiSpecificationsChangedDuringInitialRender()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var api = Path.Combine(root, "api");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(api);
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Orders\n\n```openapi\n../api/orders.json\n```\n");
        // Keep startup rendering busy after README so the external edit overlaps the initial build.
        File.WriteAllText(Path.Combine(docs, "Slow.md"), "# Slow\n\n" + new string('x', 5_000_000));
        var specificationPath = Path.Combine(api, "orders.json");
        File.WriteAllText(specificationPath, OpenApiSpecification("API Before"));

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pagePath = Path.Combine(site, "index.html");
        var pageUrl = $"http://127.0.0.1:{port}/index.html";

        try
        {
            await WaitForFileContainsAsync(pagePath, "API Before", process);
            File.WriteAllText(specificationPath, OpenApiSpecification("API After"));

            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "API After"), process);
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchesOpenApiSpecificationsOutsideTheMarkdownRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var api = Path.Combine(root, "api");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(api);
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Orders\n\n```openapi\n../api/orders.json\n```\n");
        var specificationPath = Path.Combine(api, "orders.json");
        File.WriteAllText(specificationPath, OpenApiSpecification("API Before"));

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageUrl = $"http://127.0.0.1:{port}/index.html";
        var specPageUrl = $"http://127.0.0.1:{port}/index-orders-spec.html";

        try
        {
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "API Before"), process);
            await WaitUntilAsync(async () => await PageContainsAsync(client, specPageUrl, "API Before"), process);

            File.WriteAllText(specificationPath, OpenApiSpecification("API After"));

            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "API After"), process);
            await WaitUntilAsync(async () => await PageContainsAsync(client, specPageUrl, "API After"), process);

            var secondApi = Path.Combine(root, "second-api");
            Directory.CreateDirectory(secondApi);
            var secondSpecificationPath = Path.Combine(secondApi, "orders.json");
            File.WriteAllText(secondSpecificationPath, OpenApiSpecification("API Switched"));
            File.WriteAllText(Path.Combine(docs, "README.md"), "# Orders\n\n```openapi\n../second-api/orders.json\n```\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "API Switched"), process);

            File.WriteAllText(secondSpecificationPath, OpenApiSpecification("API Latest"));
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "API Latest"), process);

            // Removing the openapi block in a single-page edit reconciles and deletes the generated spec page.
            File.WriteAllText(Path.Combine(docs, "README.md"), "# Orders\n\nPlain page without OpenAPI.\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "Plain page without OpenAPI."), process);
            await WaitUntilAsync(async () =>
            {
                using var response = await client.GetAsync(specPageUrl);
                return response.StatusCode == HttpStatusCode.NotFound;
            }, process);

            // Creating a sibling page that claims index-orders-spec.html, then editing README.md to claim
            // index-orders-spec.html, re-renders the sibling page so its link shifts to index-orders-spec-2.html.
            File.WriteAllText(Path.Combine(docs, "index-orders.md"), $"# Sibling\n\n```openapi\n{OpenApiSpecification("index-orders")}\n```\n");
            var siblingPageUrl = $"http://127.0.0.1:{port}/index-orders.html";
            var siblingShiftedSpecUrl = $"http://127.0.0.1:{port}/index-orders-spec-2.html";
            await WaitUntilAsync(async () => await PageContainsAsync(client, siblingPageUrl, "href=\"index-orders-spec.html\""), process);

            File.WriteAllText(Path.Combine(docs, "README.md"), "# Orders\n\n```openapi\n../second-api/orders.json\n```\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl, "href=\"index-orders-spec.html\""), process);
            await WaitUntilAsync(async () => await PageContainsAsync(client, siblingPageUrl, "href=\"index-orders-spec-2.html\""), process);
            await WaitUntilAsync(async () => await PageContainsAsync(client, specPageUrl, "API Latest"), process);
            await WaitUntilAsync(async () => await PageContainsAsync(client, siblingShiftedSpecUrl, "index-orders"), process);
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReloadsExternalGlossaryChangesAndConfigurationPathChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var shared = Path.Combine(root, "shared");
        var alternate = Path.Combine(root, "alternate");
        var site = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(alternate);
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Home\n\nCPU.\n");
        File.WriteAllText(Path.Combine(docs, "mdu.yml"), "glossary: ../shared/terms.md\n");
        var glossaryPath = Path.Combine(shared, "terms.md");
        var alternateGlossaryPath = Path.Combine(alternate, "terms.md");
        File.WriteAllText(glossaryPath, "*[CPU]: Central processing unit\n");
        File.WriteAllText(alternateGlossaryPath, "*[CPU]: Alternate processing unit\n");

        var (process, port) = await StartServerOnFreePortAsync(docs, site);
        using var _ = process;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageUrl = $"http://127.0.0.1:{port}/index.html";

        try
        {
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl,
                "title=\"Central processing unit\""), process);

            File.WriteAllText(glossaryPath, "*[CPU]: General processing unit\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl,
                "title=\"General processing unit\""), process);

            File.WriteAllText(Path.Combine(docs, "mdu.yml"), "glossary: ../alternate/terms.md\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl,
                "title=\"Alternate processing unit\""), process);

            File.WriteAllText(alternateGlossaryPath, "*[CPU]: Updated alternate processing unit\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl,
                "title=\"Updated alternate processing unit\""), process);

            File.WriteAllText(Path.Combine(docs, "mdu.yml"), "glossary: ../missing/nested/terms.md\n");
            await Task.Delay(250);
            var missingGlossaryDirectory = Path.Combine(root, "missing", "nested");
            Directory.CreateDirectory(missingGlossaryDirectory);
            await Task.Delay(100);
            File.WriteAllText(Path.Combine(missingGlossaryDirectory, "terms.md"), "*[CPU]: Recreated glossary\n");
            await WaitUntilAsync(async () => await PageContainsAsync(client, pageUrl,
                "title=\"Recreated glossary\""), process);
        }
        finally
        {
            StopServer(process);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForFileContainsAsync(string path, string text, Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"Preview process exited with code {process.ExitCode}.");

            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Contains(text, StringComparison.Ordinal)) return;
            }
            catch (IOException)
            {
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("Timed out waiting for the rendered preview file.");
    }

    private static async Task<bool> PageContainsAsync(HttpClient client, string url, string text)
    {
        using var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode &&
               (await response.Content.ReadAsStringAsync()).Contains(text, StringComparison.Ordinal);
    }

    private static string OpenApiSpecification(string title) => $$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "{{title}}", "version": "1.0" },
          "paths": {}
        }
        """;

    private static string RunGit(string workingDirectory, IReadOnlyList<string> arguments) =>
        TryRunGit(workingDirectory, arguments, out var error) ?? throw new InvalidOperationException(error);

    private static string? TryRunGit(string workingDirectory, IReadOnlyList<string> arguments) =>
        TryRunGit(workingDirectory, arguments, out _);

    private static string? TryRunGit(string workingDirectory, IReadOnlyList<string> arguments, out string error)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errorOutput = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        error = errorOutput.Result;
        return process.ExitCode == 0 ? output.Result : null;
    }

    private static Process StartServer(
        string docs,
        string site,
        int port,
        TaskCompletionSource<bool> ready,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Mdu/Mdu.csproj"));
        // The test runs the mdu build of its own configuration, so this is the build that `dotnet test` produced.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add(docs);
        startInfo.ArgumentList.Add(site);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--no-browser");
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the preview process.");
        // mdu prints this line only after it has bound the port.
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data?.StartsWith("Serving ", StringComparison.Ordinal) == true) ready.TrySetResult(true);
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => ready.TrySetResult(false);
        if (process.HasExited) ready.TrySetResult(false);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"Preview process exited with code {process.ExitCode}.");

            try
            {
                if (await condition()) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for the preview site to update.");
    }

    /// <summary>
    /// Starts the preview on a free port. The port is released before the server binds it, so another process can take
    /// it in between; the server then exits, and the start is retried on a new port. Startup counts as done when mdu
    /// reports that it serves, which it does only after binding the port.
    /// </summary>
    private static async Task<(Process Process, int Port)> StartServerOnFreePortAsync(
        string docs,
        string site,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var port = FindAvailablePort();
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var process = StartServer(docs, site, port, ready, environment);
            var completed = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != ready.Task || ready.Task.Result || attempt == 5) return (process, port);
            process.Dispose();
        }
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void StopServer(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
    }
}
