using System.Xml.Linq;
using Markdig;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Renderer.Test;

public class DiagramTest
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseStaticCodeHighlighter()
        .Build();

    private static string Render(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline).ReplaceLineEndings("\n");

    [Fact]
    public void RepeatedStateDiagramsDescribeThemselvesWithUniqueStableIds()
    {
        const string diagram = "```mermaid\nstateDiagram-v2\naccDescr: Lifecycle description\nstate Review {\naccDescr: Composite description\nPending\n}\nclick Pending href pending.md\n```\n";
        var markdown = diagram + "\n" + diagram;
        var html = Render(markdown);
        Assert.Equal(html, Render(markdown));
        var page = XElement.Parse("<div>" + html + "</div>");
        var svgs = page.Descendants().Where(element => element.Name.LocalName == "svg").ToArray();
        Assert.Equal(2, svgs.Length);
        var ids = page.Descendants().Attributes("id").Select(attribute => attribute.Value).ToArray();
        Assert.Equal(4, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (var svg in svgs)
        {
            Assert.Equal("Lifecycle description", svg.Elements().Single(element =>
                element.Attribute("id")?.Value == svg.Attribute("aria-describedby")?.Value).Value);
            foreach (var described in svg.DescendantsAndSelf().Where(element => element.Attribute("aria-describedby") != null))
                Assert.Single(svg.Descendants().Where(element => element.Attribute("id")?.Value == described.Attribute("aria-describedby")?.Value));
            Assert.Contains(svg.Descendants(), element => element.Name.LocalName == "a" && element.Attribute("href")?.Value == "pending.html");
        }
    }

    [Fact]
    public void RendersMermaidFlowchartsAsInlineSvg()
    {
        var html = Render("```mermaid\nflowchart LR\n    A --> B\n```\n");

        Assert.StartsWith("<figure class=\"diagram\"><svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-flowchart\"", html);
        Assert.EndsWith("</svg></figure>\n", html);
        Assert.DoesNotContain("<pre", html);
        Assert.DoesNotContain("class=\"mermaid\"", html);
    }

    [Fact]
    public void RendersMermaidSequenceDiagramsAsInlineSvg()
    {
        var html = Render("```mermaid\nsequenceDiagram\n    A->>B: Hello\n```\n");

        Assert.StartsWith("<figure class=\"diagram\"><svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-sequence\"", html);
        Assert.DoesNotContain("<pre", html);
    }

    [Fact]
    public void RendersStateLinksAndDescriptionsAfterCommentsAndFrontMatter()
    {
        var html = Render("```mermaid\n%% Lifecycle\n---\ntitle: Options\n---\nstateDiagram-v2\nstate \"In review\" as Review\nReview: Waiting for approval\nclick Review href guide.md#state;\n```\n");
        Assert.Contains("class=\"diagram-state\"", html);
        Assert.Contains("href=\"guide.html#state\"", html);
        Assert.Contains("In review", html);
        Assert.Contains("Waiting for approval", html);
        Assert.DoesNotContain("<pre", html);
    }

    [Theory]
    [InlineData("stateDiagram")]
    [InlineData("stateDiagram-v2")]
    public void RendersMermaidStateDiagramsAndClickableStatesAsInlineSvg(string header)
    {
        var html = Render($"```mermaid\n{header}\n[*] --> A\nclick A href \"guide.md#state\"\n```\n");

        Assert.Contains("class=\"diagram-state\"", html);
        Assert.Contains("class=\"diagram-link\" href=\"guide.html#state\"", html);
        Assert.DoesNotContain("<pre", html);
    }

    [Theory]
    [InlineData("A -->")]
    [InlineData("state A {\nB")]
    [InlineData("state A {\nB\n--\nC\n}")]
    [InlineData("A\nnote right of A: Unsupported")]
    public void KeepsInvalidAndUnsupportedStateDiagramsAsCode(string statements)
    {
        var html = Render($"```mermaid\nstateDiagram-v2\n{statements}\n```\n");

        Assert.Contains("<pre", html);
        Assert.DoesNotContain("<figure class=\"diagram\"", html);
    }

    [Fact]
    public void RendersContextDiagramsAsInlineSvg()
    {
        var html = Render("```context\nApp[App]\nactor User\nUser --> App\n```\n");

        Assert.StartsWith("<figure class=\"diagram\"><svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-context\"", html);
        Assert.DoesNotContain("<pre", html);
    }

    [Fact]
    public void RendersPlantUmlComponentDiagramsAsInlineSvg()
    {
        var html = Render("```plantuml\n@startuml\n[App] --> [Database]\n@enduml\n```\n");

        Assert.StartsWith("<figure class=\"diagram\"><svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-component\"", html);
        Assert.DoesNotContain("<pre", html);
    }

    [Fact]
    public void KeepsUnsupportedMermaidDiagramsAsCodeBlocks()
    {
        var html = Render("```mermaid\npie\n    \\\"Dogs\\\" : 1\n```\n");

        Assert.Contains("<pre", html);
        Assert.Contains("pie", html);
        Assert.DoesNotContain("<figure class=\"diagram\"", html);
    }
}
