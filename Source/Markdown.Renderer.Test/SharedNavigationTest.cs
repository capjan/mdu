using System.Diagnostics;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class SharedNavigationTest
{
    [BrowserFact]
    public async Task SharedNavigationMatchesStaticStateWithNestedEscapedLinksInAFileBrowser()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-navigation-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Guide", "Part");
        Directory.CreateDirectory(folder);
        NavigationItem[] items =
        [
            new() { Title = "Home", Url = "index.html", IsOutsideTopNavigationSection = true, IsCurrentSection = true, IsAncestorOfCurrentTopSection = true },
            new() { Title = "Guide <&>", Url = "Guide/index.html", Depth = 1, IsTopNavigationItem = true, IsCurrentSection = true, IsInCurrentTopSection = true, TopNavigationOwnerUrl = "Guide/index.html" },
            new() { Title = "Part", Url = "Guide/Part/index.html", Depth = 2, IsCurrentSection = true, IsInCurrentTopSection = true, TopNavigationOwnerUrl = "Guide/index.html" },
            new() { Title = "Page <script> & \"quoted\"", Url = "Guide/Part/Page%20%26%20%23.html", Depth = 3, IsCurrent = true, IsCurrentSection = true, IsInCurrentTopSection = true, TopNavigationDepth = 1, TopNavigationOwnerUrl = "Guide/index.html" },
            // A top entry nested in the tree owns its own section, not the enclosing one.
            new() { Title = "Nested", Url = "Guide/Nested/index.html", Depth = 2, IsTopNavigationItem = true, TopNavigationOwnerUrl = "Guide/Nested/index.html" },
            new() { Title = "Nested page", Url = "Guide/Nested/Page.html", Depth = 3, TopNavigationDepth = 1, TopNavigationOwnerUrl = "Guide/Nested/index.html" },
            new() { Title = "Other", Url = "Other.html", Depth = 1, IsOutsideTopNavigationSection = true }
        ];
        try
        {
            var renderer = new HtmlRenderer();
            renderer.PrepareBasePath(root);
            renderer.PrepareNavigation(items);
            foreach (var item in items) item.Url = "../../" + item.Url;
            using var writer = new StringWriter();
            await renderer.WriteHtml(writer, "Page", "", 2, "# Page\n\n## Details\n", "", items,
                topNavigationItems: [items[1]]);
            var expected = SiteNavigation.RenderItems(items);
            var check = "<template id=\"expected\">" + expected + "</template><script>" + """
                (() => {
                    const state = container => [...container.querySelectorAll('.site-nav-link')].map(link => {
                        const item = link.closest('li');
                        return [new URL(link.getAttribute('href'), document.baseURI).href, link.textContent,
                            link.getAttribute('aria-current'), [...link.classList].sort(),
                            [...link.parentElement.classList].sort(), [...item.classList].sort(),
                            item.style.getPropertyValue('--wide-indent-depth').trim()];
                    });
                    const actual = document.getElementById('site-navigation');
                    const expected = document.getElementById('expected').content;
                    const matches = JSON.stringify(state(actual)) === JSON.stringify(state(expected));
                    const active = actual.querySelector('[aria-current="page"]');
                    const open = [...actual.querySelectorAll('ul')].filter(ul => ul.contains(active)).every(ul => !ul.hidden);
                    document.body.dataset.navigationTest = matches && open ? 'passed' : JSON.stringify({actual:state(actual),expected:state(expected),open});
                })();
                """ + "</script>";
            var page = Path.Combine(folder, "Page & #.html");
            await File.WriteAllTextAsync(page, writer.ToString().Replace("</body>", check + "</body>"));
            var start = new ProcessStartInfo(ImageLightboxTest.FindBrowser()!) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check",
                "--user-data-dir=" + Path.Combine(root, "profile"), "--virtual-time-budget=2000", "--dump-dom", new Uri(page).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("data-navigation-test=\"passed\"", await output);
            Assert.DoesNotContain("Uncaught", await errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
