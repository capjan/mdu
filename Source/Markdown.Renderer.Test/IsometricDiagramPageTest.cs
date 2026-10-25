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

    [Theory]
    [InlineData("```mermaid\nflowchart LR\nA --> B\n```\n")]
    [InlineData("```isometric\na --> b\n```\n")]
    [InlineData("```isometric\nphase One\na --> a\n```\n")]
    public async Task LeavesThePlaybackScriptOutOfPagesWithoutPhaseControls(string block)
    {
        var page = await RenderPage($"# Home\n\n{block}");

        Assert.DoesNotContain("document.querySelectorAll(\".iso\")", page);
    }

    [Fact]
    public async Task AddsThePlaybackScriptRegardlessOfTheLanguageCase()
    {
        var page = await RenderPage("# Home\n\n```Isometric\nphase One\na --> b\n```\n");

        Assert.Contains("document.querySelectorAll(\".iso\")", page);
    }

    private static async Task<string> RenderPage(string markdown)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await new HtmlRenderer().WriteHtml(writer, "Test", "", 0, markdown, "");
        return html.ToString();
    }
}
