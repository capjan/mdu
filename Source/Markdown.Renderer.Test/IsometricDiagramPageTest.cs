using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class IsometricDiagramPageTest
{
    [Fact]
    public async Task RendersTheDiagramAndAddsThePlaybackScript()
    {
        var page = await RenderPage("# Home\n\n```isometric\nphase One\na --> b\nphase Two\nb --> c\n```\n");

        Assert.Contains("<figure class=\"diagram\"><div class=\"iso\">", page);
        Assert.Contains("class=\"iso-play\"", page);
        Assert.Contains("document.querySelectorAll(\".iso\")", page);
    }

    [Fact]
    public async Task KeepsThePhaseControlsOfEqualDiagramsApart()
    {
        const string diagram = "```isometric\nphase One\na --> b\nphase Two\nb --> c\n```\n";
        var page = await RenderPage($"# Home\n\n{diagram}\nText\n\n{diagram}");

        var names = Regex.Matches(page, "<input type=\"radio\" name=\"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .Distinct()
            .ToList();
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public async Task LeavesThePlaybackScriptOutOfOtherPages()
    {
        var page = await RenderPage("# Home\n\n```mermaid\nflowchart LR\nA --> B\n```\n");

        Assert.DoesNotContain("document.querySelectorAll(\".iso\")", page);
    }

    private static async Task<string> RenderPage(string markdown)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await new HtmlRenderer().WriteHtml(writer, "Test", "", 0, markdown, "");
        return html.ToString();
    }
}
