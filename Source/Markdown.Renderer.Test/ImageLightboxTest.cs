using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class ImageLightboxTest
{
    private const string ScriptMarker = "js/image-lightbox.js";

    private static async Task<string> RenderAsync(string markdown, bool imageZoom = true)
    {
        await using var writer = new StringWriter();
        await new HtmlRenderer().WriteHtml(writer, "test", "", 0, markdown, "", imageZoom: imageZoom);
        return writer.ToString();
    }

    [Fact]
    public async Task AddsTheViewerOnlyToPagesWithImages()
    {
        Assert.Contains(ScriptMarker, await RenderAsync("# Title\n\n![Alt](image.png)\n"));
        Assert.Contains(ScriptMarker, await RenderAsync("# Title\n\n<IMG src=\"image.png\">\n"));
        Assert.DoesNotContain(ScriptMarker, await RenderAsync("# Title\n\nNo image, only `<img>` as code.\n"));
    }

    [Theory]
    [InlineData("---\nimages.zoom: false\n---", false)]
    [InlineData("---\nimages:\n  zoom: false\n---", false)]
    [InlineData("---\nimages.zoom: true\n---", true)]
    [InlineData("---\ntitle: Images\n---", true)]
    [InlineData("---\nimages.zoom: maybe\n---", true)]
    [InlineData("---\nimages: [\n---", true)]
    [InlineData("", true)]
    public void ReadsThePageSettingFromFrontMatter(string frontMatter, bool expected)
    {
        Assert.Equal(expected, ImageZoom.ReadFrontMatter(frontMatter));
    }

    [Theory]
    [InlineData("---\nimages.zoom: off\n---", "images.zoom must be 'true' or 'false', not 'off'.")]
    [InlineData("---\nimages:\n  zoom: 1\n---", "images.zoom must be 'true' or 'false', not '1'.")]
    [InlineData("---\nimages: false\n---", "Use 'images.zoom: false' instead of 'images: false'.")]
    [InlineData("---\nimages: [cover.png]\n---", null)]
    [InlineData("---\nimages: cover.png\n---", null)]
    [InlineData("---\nimages: false\nimages.zoom: false\n---", null)]
    [InlineData("---\nimages.zoom: false\n---", null)]
    [InlineData("---\ntitle: Images\n---", null)]
    public void FindsProblemsWithThePageSetting(string frontMatter, string? expected)
    {
        Assert.Equal(expected, ImageZoom.FindProblem(frontMatter));
    }

    [Fact]
    public void LeavesFootnotePreviewsOutOfTheTabOrder()
    {
        Assert.Contains(".footnote-preview", Resources.ImageLightboxScriptContent());
    }

    [Fact]
    public void LeavesModifierClicksOnLinksToTheBrowser()
    {
        Assert.Contains("event.metaKey || event.ctrlKey || event.shiftKey || event.altKey", Resources.ImageLightboxScriptContent());
    }

    [Fact]
    public async Task LeavesTheViewerOutWhenThePageTurnsItOff()
    {
        var html = await RenderAsync("# Title\n\n![Alt](image.png)\n", imageZoom: false);

        Assert.DoesNotContain(ScriptMarker, html);
    }

    [Fact]
    public async Task KeepsTheViewerForImagesThatTurnItOnAgain()
    {
        var html = await RenderAsync("# Title\n\n![Alt](image.png){.image-zoom-on}\n", imageZoom: false);

        Assert.Contains("class=\"image-zoom-on\"", html);
        Assert.Matches("<script defer src=\"js/image-lightbox\\.js\\?v=[0-9a-f]{12}\" data-image-zoom=\"off\"></script>", html);
    }

    [Fact]
    public async Task LetsSingleImagesTurnTheViewerOff()
    {
        var html = await RenderAsync("# Title\n\n![Logo](logo.png){.image-zoom-off}\n");

        Assert.Contains("class=\"image-zoom-off\"", html);
        Assert.Contains("image.closest(\".image-zoom-on, .image-zoom-off\")", Resources.ImageLightboxScriptContent());
        Assert.Contains(ScriptMarker, html);
    }

    [Fact]
    public void OffersZoomControlsThatStayFocusable()
    {
        var script = Resources.ImageLightboxScriptContent();

        foreach (var action in new[] { "in", "out", "fit", "actual" })
            Assert.Contains($"data-zoom=\"{action}\"", script);
        Assert.Contains("role=\"group\"", script);
        Assert.Contains("setAttribute(\"aria-disabled\"", script);
        Assert.DoesNotContain(".disabled", script);
    }

    [Fact]
    public async Task RendersImagesAndFiguresInsideTheOptInGallery()
    {
        var html = await RenderAsync("""
            <div class="image-gallery">

            ![Overview](light.svg#only-light)
            ![Overview](dark.svg#only-dark)

            ^^^
            ![Settings](settings.svg)
            ^^^ Choose an option.

            </div>
            """);

        Assert.Contains("<div class=\"image-gallery\">", html);
        Assert.Contains("src=\"light.svg#only-light\"", html);
        Assert.Contains("src=\"dark.svg#only-dark\"", html);
        Assert.Contains("<figure>", html);
        Assert.Contains("<figcaption>Choose an option.</figcaption>", html);
        Assert.Contains(ScriptMarker, html);
    }

    [Fact]
    public async Task KeepsInlineGalleryNavigationWhenTheViewerIsOff()
    {
        var html = await RenderAsync("""
            <div class="image-gallery">

            ![First step](first.png)

            ![Second step](second.png)

            </div>
            """, imageZoom: false);

        Assert.Contains(ScriptMarker, html);
        Assert.Contains("data-image-zoom=\"off\"", html);
    }

    [Fact]
    public void OffersHiddenGalleryControlsWithAccessibleNamesAndPosition()
    {
        var script = Resources.ImageLightboxScriptContent();

        Assert.Contains("aria-label=\"Image gallery\" hidden", script);
        Assert.Contains("aria-label=\"Previous image\"", script);
        Assert.Contains("aria-label=\"Next image\"", script);
        Assert.Contains("aria-live=\"polite\" aria-atomic=\"true\"", script);
        Assert.Contains(".image-lightbox-gallery[hidden]", Resources.CssContent());
    }

    [Fact]
    public void KeepsThePageFromScrollingWhileTheViewerIsOpen()
    {
        Assert.Matches(@"html:has\(\.image-lightbox\[open\]\)\s*\{\s*overflow:\s*hidden;\s*scrollbar-gutter:\s*auto;\s*\}", Resources.CssContent());
    }

    [Fact]
    public void ShowsAZoomCursorOnImagesThatOpenTheViewer()
    {
        Assert.Contains("image.classList.toggle(\"image-zoomable\", opens)", Resources.ImageLightboxScriptContent());
        Assert.Matches(@"\.image-zoomable\s*\{\s*cursor:\s*zoom-in;\s*\}", Resources.CssContent());
    }

    [BrowserFact]
    public async Task DoesNotMakeThemeHiddenImagesFocusableInARealBrowser()
    {
        var browser = FindBrowser() ?? throw new InvalidOperationException("The browser test was not skipped when Chrome or Chromium was unavailable.");
        var themeScript = Resources.ScriptContent("theme");
        Assert.NotEmpty(themeScript);

        var directory = Path.Combine(Path.GetTempPath(), "mdu-image-lightbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var page = Path.Combine(directory, "index.html");
            var html = """
                <!doctype html>
                <html data-theme="auto"><head><style>
                """ + Resources.CssContent() + """
                </style></head><body>
                <script>
                    window.fakeSystemTheme = {
                        matches: false,
                        change: null,
                        addEventListener(event, callback) { this.change = callback; }
                    };
                    window.matchMedia = () => window.fakeSystemTheme;
                </script>
                <script>
                """ + themeScript + """
                </script>
                <main class="markdown-content">
                    <img id="hidden" alt="Dark image" class="image-zoom-on" width="100" height="100" src="dark.svg#only-dark">
                    <img id="visible" alt="Light image" class="image-zoom-on" width="100" height="100" src="light.svg#only-light">
                </main>
                <script>
                """ + Resources.ImageLightboxScriptContent() + """
                </script>
                <script>
                    const dark = document.getElementById("hidden");
                    const light = document.getElementById("visible");
                    const record = (scheme, hidden, visible) => {
                        document.body.dataset[`${scheme}HiddenDisplay`] = getComputedStyle(hidden).display;
                        document.body.dataset[`${scheme}HiddenTrigger`] = String(hidden.classList.contains("image-lightbox-trigger"));
                        document.body.dataset[`${scheme}VisibleTrigger`] = String(visible.classList.contains("image-lightbox-trigger"));
                        document.body.dataset[`${scheme}HiddenTabIndex`] = String(hidden.tabIndex);
                        document.body.dataset[`${scheme}VisibleTabIndex`] = String(visible.tabIndex);
                        document.activeElement?.blur();
                        hidden.focus();
                        document.body.dataset[`${scheme}HiddenFocused`] = String(document.activeElement === hidden);
                        visible.focus();
                        document.body.dataset[`${scheme}VisibleFocused`] = String(document.activeElement === visible);
                    };
                    const waitFor = (condition, done, attempts = 80) => {
                        if (condition()) return done();
                        if (attempts === 0) {
                            document.body.dataset.browserTest = "timeout";
                            return;
                        }
                        setTimeout(() => waitFor(condition, done, attempts - 1), 20);
                    };
                    waitFor(
                        () => document.documentElement.dataset.colorScheme === "light" &&
                            getComputedStyle(dark).display === "none" && !dark.classList.contains("image-lightbox-trigger") &&
                            light.classList.contains("image-lightbox-trigger"),
                        () => {
                            record("light", dark, light);
                            fakeSystemTheme.matches = true;
                            fakeSystemTheme.change();
                            waitFor(
                                () => document.documentElement.dataset.colorScheme === "dark" &&
                                    getComputedStyle(light).display === "none" && !light.classList.contains("image-lightbox-trigger") &&
                                    dark.classList.contains("image-lightbox-trigger"),
                                () => {
                                    record("dark", light, dark);
                                    document.body.dataset.browserTest = "complete";
                                });
                        });
                </script>
                </body></html>
                """;
            await File.WriteAllTextAsync(page, html);

            var start = new ProcessStartInfo(browser) { RedirectStandardOutput = true };
            foreach (var argument in new[]
                     {
                         "--headless=new", "--no-sandbox", "--disable-gpu", "--no-first-run",
                         "--user-data-dir=" + Path.Combine(directory, "profile"), "--virtual-time-budget=5000",
                         "--dump-dom", new Uri(page).AbsoluteUri
                     })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            var output = await outputTask;

            Assert.Equal(0, process.ExitCode);
            var body = Regex.Match(output, @"<body\b[^>]*>").Value;
            string Attribute(string name) => Regex.Match(body, $@"\bdata-{Regex.Escape(name)}=""([^""]*)""").Groups[1].Value;
            Assert.True(Attribute("browser-test") == "complete", $"The browser test did not complete: {body}");
            foreach (var scheme in new[] { "light", "dark" })
            {
                Assert.Equal("none", Attribute($"{scheme}-hidden-display"));
                Assert.True(Attribute($"{scheme}-hidden-trigger") == "false", $"Unexpected {scheme} theme image state: {body}");
                Assert.Equal("true", Attribute($"{scheme}-visible-trigger"));
                Assert.Equal("-1", Attribute($"{scheme}-hidden-tab-index"));
                Assert.Equal("0", Attribute($"{scheme}-visible-tab-index"));
                Assert.Equal("false", Attribute($"{scheme}-hidden-focused"));
                Assert.Equal("true", Attribute($"{scheme}-visible-focused"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UsesTheSharedFocusRuleForViewerButtons()
    {
        var css = Resources.CssContent();
        var sharedFocusRule = css[..css.IndexOf("outline: 2px solid var(--link);", StringComparison.Ordinal)];

        Assert.Contains(".image-lightbox button:focus-visible", sharedFocusRule);
        Assert.Contains(".image-lightbox-trigger:focus-visible", sharedFocusRule);
    }

    internal static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("MDU_TEST_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured)) return CanLaunch(configured) ? configured : null;

        configured = Environment.GetEnvironmentVariable("MDU_BENCHMARK_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured) && CanLaunch(configured)) return configured;

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"]
            : OperatingSystem.IsWindows()
                ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google/Chrome/Application/chrome.exe")]
                : ["google-chrome", "chromium", "chromium-browser"];
        return candidates.FirstOrDefault(CanLaunch);
    }

    private static bool CanLaunch(string executable) =>
        File.Exists(executable) || Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, executable))) == true;
}

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (ImageLightboxTest.FindBrowser() is null)
            Skip = "Chrome or Chromium is required for this browser test.";
    }
}
