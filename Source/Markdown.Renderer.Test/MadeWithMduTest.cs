using System.Text;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class MadeWithMduTest
{
    [Fact]
    public async Task RendersMadeWithMduCreditLinkedToProjectPage()
    {
        var html = await RenderAsync(new SiteConfiguration());

        Assert.Contains("<p class=\"made-with-mdu\">Made with <a href=\"https://capjan.github.io/mdu/index.html\" target=\"_blank\" rel=\"noopener noreferrer\">MDU</a></p>", html);
    }

    private static async Task<string> RenderAsync(SiteConfiguration configuration)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var renderer = new HtmlRenderer();
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);

        try
        {
            renderer.PrepareBasePath(outputPath, configuration);
            await renderer.WriteHtml(writer, "test", "", 0, "# Test", "");
            return sb.ToString();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }
}
