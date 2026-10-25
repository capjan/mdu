using System.Text;

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
    }
}
