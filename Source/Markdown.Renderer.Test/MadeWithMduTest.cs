using System.Text;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class MadeWithMduTest
{
    [Fact]
    public async Task RendersMadeWithMduCreditLinkedToProjectPage()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Test", "");

        Assert.Contains("<footer class=\"made-with-mdu\">Made with <a href=\"https://capjan.github.io/mdu/index.html\" target=\"_blank\" rel=\"noopener noreferrer\">MDU</a></footer>", sb.ToString());
        var html = sb.ToString();
        Assert.True(html.IndexOf("class=\"made-with-mdu\"", StringComparison.Ordinal) <
                    html.IndexOf("</article>", StringComparison.Ordinal));
        Assert.DoesNotContain("%%ATTRIBUTION%%", html);
    }

    [Fact]
    public async Task OmitsCreditWhenDisabled()
    {
        var output = Path.Combine(Path.GetTempPath(), $"mdu-attribution-{Guid.NewGuid():N}");
        try
        {
            var renderer = new HtmlRenderer();
            renderer.PrepareBasePath(output, new SiteConfiguration { AttributionVisible = false });
            using var writer = new StringWriter();
            await renderer.WriteHtml(writer, "test", "", 0, "# Test", "");

            Assert.DoesNotContain("made-with-mdu", writer.ToString());
            Assert.DoesNotContain("Made with", writer.ToString());
            Assert.DoesNotContain("%%ATTRIBUTION%%", writer.ToString());
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }
}
