using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class SiteAssetsTest
{
    [Fact]
    public async Task OptionalAssetsAreSharedAndHeroEffectsRemainSelective()
    {
        var output = Path.Combine(Path.GetTempPath(), "mdu-site-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var renderer = new HtmlRenderer();
            renderer.PrepareBasePath(output);
            using var plain = new StringWriter();
            await renderer.WriteHtml(plain, "Plain", "", 0, "# Plain", "");
            Assert.False(File.Exists(Path.Combine(output, "js", "hero.js")));
            Assert.False(File.Exists(Path.Combine(output, "css", "hero.css")));
            const string markdown = "# Banner\n\n```hero\neffect: beach\ntitle: Welcome\n```\n";
            using var first = new StringWriter();
            await renderer.WriteHtml(first, "First", "", 0, markdown, "");
            Assert.Contains("src=\"js/hero-beach.js?v=", first.ToString());
            Assert.Contains("href=\"css/hero.css?v=", first.ToString());
            Assert.False(File.Exists(Path.Combine(output, "js", "hero-effects.js")));
            Assert.False(File.Exists(Path.Combine(output, "js", "hero-playground.js")));
            var timestamp = File.GetLastWriteTimeUtc(Path.Combine(output, "js", "hero.js"));
            using var second = new StringWriter();
            await renderer.WriteHtml(second, "Second", "", 2, markdown, "");
            Assert.Contains("src=\"../../js/hero.js?v=", second.ToString());
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(output, "js", "hero.js")));
            Assert.DoesNotContain("<style>", second.ToString());
            Assert.DoesNotContain("<script>", second.ToString());
            Assert.Equal(renderer.RenderPdfMarkdownFragment(markdown), new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

            renderer.PrepareBasePath(output, new SiteConfiguration { Light = new SitePalette { Primary = "#123456" } });
            using var themed = new StringWriter();
            await renderer.WriteHtml(themed, "Themed", "", 0, "# Themed", "");
            Assert.Contains("--link:#123456", File.ReadAllText(Path.Combine(output, "css", "theme.css")));
            renderer.PrepareBasePath(output, new SiteConfiguration { Light = new SitePalette { Primary = "#abcdef" } });
            using var changed = new StringWriter();
            await renderer.WriteHtml(changed, "Changed", "", 0, "# Changed", "");
            Assert.Contains("--link:#abcdef", File.ReadAllText(Path.Combine(output, "css", "theme.css")));
            // Changed content gets a new URL, so browsers and CDNs cannot keep serving the old file.
            var version = new System.Text.RegularExpressions.Regex("css/theme\\.css\\?v=([0-9a-f]{12})");
            Assert.NotEqual(version.Match(themed.ToString()).Groups[1].Value, version.Match(changed.ToString()).Groups[1].Value);
            Assert.Contains(Path.Combine(output, "css", "theme.css"), renderer.SiteAssetPaths);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }
}
