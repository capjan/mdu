using System.Xml.Linq;
using Xunit;

namespace Markdown.Confluence.Test;

public class ConfluenceStorageRendererTest
{
    private static readonly ConfluenceTarget Target = new("https://wiki.example.test/confluence", "DOC", "10");

    [Fact]
    public void ProducesStorageMacrosAndStableAnchors()
    {
        var page = Source("# Home\n\n[Next](Next.md#details)\n\n```csharp\nvar x = \"<&\";\n```\n\n> [!NOTE]\n> A note\n\n- [x] Done\n\n++ctrl+c++");
        var next = Source("## Details") with { Path = "Next.md", Title = "Next" };
        var result = new ConfluenceStorageRenderer(Target, [page, next]).Render(page);
        var xml = Parse(result.Storage);
        XNamespace ac = "http://atlassian.com/content";
        XNamespace ri = "http://atlassian.com/resource/identifier";
        Assert.Contains(xml.Descendants(ac + "structured-macro"), element => element.Attribute(ac + "name")?.Value == "code");
        Assert.Contains(xml.Descendants(ac + "structured-macro"), element => element.Attribute(ac + "name")?.Value == "anchor");
        Assert.Contains(xml.Descendants(ac + "structured-macro"), element => element.Attribute(ac + "name")?.Value == "panel");
        Assert.Equal(Target.PageTitle(next), xml.Descendants(ri + "page").Single().Attribute(ri + "content-title")?.Value);
        Assert.Contains("[x]", xml.Value);
        Assert.DoesNotContain("<script", result.Storage);
    }

    [Fact]
    public void RendersHtmlSourceAsTextAndNeverExecutesIt()
    {
        var page = Source("# Home\n\n<script>alert(1)</script>");
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        Assert.Empty(Parse(result.Storage).Descendants("script"));
        Assert.Contains("&lt;script&gt;", result.Storage);
    }

    [Fact]
    public void PreservesUnsupportedInteractiveBlockAsSourceWithDiagnostic()
    {
        var page = Source("```hero\nscene: beach\n```");
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        Assert.Single(result.Diagnostics);
        Assert.Contains("scene: beach", result.Storage);
    }

    [Fact]
    public void ExportsDiagramAsDeterministicSelfContainedAttachment()
    {
        var page = Source("```mermaid\nflowchart LR\nA --> B\n```");
        var renderer = new ConfluenceStorageRenderer(Target, [page]);
        var result = renderer.Render(page);
        Assert.True(result.Assets.Count == 1, result.Storage);
        var asset = Assert.Single(result.Assets);
        Assert.EndsWith(".svg", asset.Name);
        Assert.Equal(asset.Name, Assert.Single(renderer.Render(page).Assets).Name);
        var svg = XElement.Parse(System.Text.Encoding.UTF8.GetString(asset.Content));
        Assert.Contains(svg.Elements(), element => element.Name.LocalName == "style");
        Assert.Contains("ri:attachment", result.Storage);
    }

    [Fact]
    public void UploadsLocalImagesAndFilesUsingContentAddressedNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mdu-confluence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "image.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(directory, "file.txt"), "attachment");
            var page = Source("![Alt](image.png)\n\n[File](file.txt)") with
            {
                FilePath = Path.Combine(directory, "README.md"), SourceRoot = directory
            };
            var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
            Assert.Equal(2, result.Assets.Count);
            Assert.All(result.Assets, asset => Assert.StartsWith("mdu-", asset.Name));
            Assert.Contains("ac:alt=\"Alt\"", result.Storage);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("[Bad](../outside.md)")]
    [InlineData("[Bad](Missing.md)")]
    [InlineData("![Bad](../../secret.png)")]
    [InlineData("[Bad](javascript:alert)")]
    [InlineData("![Bad](file:///etc/passwd)")]
    [InlineData("[Bad](//other.example.test/path)")]
    public void RejectsUnsafeOrUnresolvedLinks(string markdown)
    {
        var page = Source(markdown);
        Assert.Throws<InvalidOperationException>(() => new ConfluenceStorageRenderer(Target, [page]).Render(page));
    }

    [Fact]
    public void DuplicateDisplayTitlesGetDifferentDeterministicConfluenceTitles()
    {
        var first = Source("# Overview") with { Path = "A/README.md", Title = "Overview" };
        var second = first with { Path = "B/README.md" };
        Assert.NotEqual(Target.PageTitle(first), Target.PageTitle(second));
        Assert.Equal(Target.PageTitle(first), Target.PageTitle(first));
    }

    [Fact]
    public void SharedGlossaryAndLocalOverridesArePreserved()
    {
        var page = Source("# Home\n\nAPI");
        var result = new ConfluenceStorageRenderer(Target, [page], "*[API]: Shared definition").Render(page);
        Assert.Contains("title=\"Shared definition\"", result.Storage);
        page = page with { Markdown = "# Home\n\nAPI\n\n*[API]: Local definition" };
        result = new ConfluenceStorageRenderer(Target, [page], "*[API]: Shared definition").Render(page);
        Assert.Contains("title=\"Local definition\"", result.Storage);
    }

    [Fact]
    public void ConvertsStaticOpenApiReferenceAndEscapesUserHtml()
    {
        var page = Source("""
            # API

            <script>not executable</script>

            ```openapi
            openapi: 3.0.3
            info:
              title: Sample API
              version: 1.0.0
            paths:
              /ping:
                get:
                  summary: Ping
                  responses:
                    '200':
                      description: OK
            ```
            """);
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        Assert.Contains("Ping", Parse(result.Storage).Value);
        Assert.Empty(Parse(result.Storage).Descendants("script"));
        Assert.DoesNotContain("&lt;details", result.Storage);
    }

    [Fact]
    public void CodeMacroPreservesCdataTerminators()
    {
        var page = Source("```text\nsome ]]> <xml> & text\n```");
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        Assert.Contains("some ]]> <xml> & text", Parse(result.Storage).Value);
    }

    [Fact]
    public void DiagramLinksUseConfluencePageTitlesAndNamedAnchors()
    {
        var page = Source("```mermaid\nflowchart LR\nA --> B\nclick A href \"#home\"\n```");
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        var svg = XElement.Parse(System.Text.Encoding.UTF8.GetString(Assert.Single(result.Assets).Content));
        var href = svg.Descendants().Attributes("href").Single().Value;
        Assert.StartsWith("https://wiki.example.test/confluence/display/DOC/", href);
        Assert.EndsWith("-home", href);
        Assert.Contains(Uri.EscapeDataString(Target.PageTitle(page).Replace(" ", "")), href);
    }

    private static ConfluenceSource Source(string markdown) => new("README.md", null, "Home", markdown, "/docs/README.md", "/docs");
    private static XElement Parse(string storage) =>
        XElement.Parse("<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" + storage + "</root>");
}
