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
    public void OmitsSourceCommentsButPreservesCommentSyntaxInCodeAndOtherHtml()
    {
        var page = Source("""
            # Home

            <!-- markdownlint-disable -->

            Visible <!-- private inline note --> text.

            <!--
            private multiline note
            -->

            <b>HTML remains text</b>

            `<!-- literal inline code -->`

            ```html
            <!-- literal fenced code -->
            ```
            """);
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        var text = Parse(result.Storage).Value;
        Assert.DoesNotContain("markdownlint-disable", text);
        Assert.DoesNotContain("private inline note", text);
        Assert.DoesNotContain("private multiline note", text);
        Assert.Contains("Visible  text.", text);
        Assert.Contains("<!-- literal inline code -->", text);
        Assert.Contains("<!-- literal fenced code -->", text);
        Assert.Contains("<b>HTML remains text</b>", text);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../docs-other/outside.txt")]
    public void RejectsRealAttachmentsOutsideSourceRootOnEveryPlatform(string link)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mdu-confluence-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(directory, "docs");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(Path.Combine(directory, "docs-other"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "outside.txt"), "must not be read");
            File.WriteAllText(Path.Combine(directory, "docs-other", "outside.txt"), "must not be read");
            var page = Source($"![Outside]({link})") with { FilePath = Path.Combine(docs, "README.md"), SourceRoot = docs };
            var error = Assert.Throws<InvalidOperationException>(() => new ConfluenceStorageRenderer(Target, [page]).Render(page));
            Assert.Equal("Attachment path escapes its source root.", error.Message);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DeclaresSchemaVersionForEveryGeneratedMacro()
    {
        var page = Source("# Home\n\n```text\ncode\n```\n\n> [!NOTE]\n> Note");
        XNamespace ac = "http://atlassian.com/content";
        var macros = Parse(new ConfluenceStorageRenderer(Target, [page]).Render(page).Storage).Descendants(ac + "structured-macro").ToArray();
        Assert.Equal(3, macros.Length);
        Assert.All(macros, macro => Assert.Equal("1", macro.Attribute(ac + "schema-version")?.Value));
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
    public void PreservesNamedEntitiesInTextAndQuotedAttributes()
    {
        var page = Source("""
            ![Say &quot;hello&quot; &apos;world&apos; &amp; &nbsp;](https://example.test/image.png)

            [Link](https://example.test "Say &quot;hello&quot; &apos;world&apos; &amp; &nbsp;")

            Text &quot;hello&quot; &apos;world&apos; &amp; &nbsp; &copy;
            """);
        var result = new ConfluenceStorageRenderer(Target, [page]).Render(page);
        var root = Parse(result.Storage);
        XNamespace ac = "http://atlassian.com/content";
        Assert.Equal("Say \"hello\" 'world' & \u00a0", root.Descendants(ac + "image").Single().Attribute(ac + "alt")?.Value);
        Assert.Equal("Say \"hello\" 'world' & \u00a0", root.Descendants("a").Single().Attribute("title")?.Value);
        Assert.Contains("Text \"hello\" 'world' & \u00a0 \u00a9", root.Value);
    }

    [Theory]
    [InlineData("Guide.md", "./", "README.md")]
    [InlineData("Nested/Guide.md", "../", "README.md")]
    [InlineData("Guide.md", "./", "index.md")]
    [InlineData("Nested/Guide.md", "../", "index.md")]
    public void DirectoryLinksResolveToDocumentationRoot(string sourcePath, string link, string rootPath)
    {
        var home = Source("# Home") with { Path = rootPath };
        var page = Source($"[Home]({link}#home)") with { Path = sourcePath };
        var result = new ConfluenceStorageRenderer(Target, [home, page]).Render(page);
        XNamespace ri = "http://atlassian.com/resource/identifier";
        XNamespace ac = "http://atlassian.com/content";
        var root = Parse(result.Storage);
        Assert.Equal(Target.PageTitle(home), root.Descendants(ri + "page").Single().Attribute(ri + "content-title")?.Value);
        Assert.Equal("home", root.Descendants(ac + "link").Single().Attribute(ac + "anchor")?.Value);
        Assert.Empty(result.Assets);
    }

    [Fact]
    public void RejectsAliasesThatConflictWithAnotherExportedPage()
    {
        var home = Source("# Home") with { AliasPath = "Next.html" };
        var next = Source("# Next") with { Path = "Next.md" };
        var error = Assert.Throws<InvalidOperationException>(() => new ConfluenceStorageRenderer(Target, [home, next]));
        Assert.Contains("Conflicting Confluence page link target", error.Message);
    }

    [Theory]
    [InlineData("Guide.md", "./", "README.md")]
    [InlineData("Nested/Guide.md", "../", "index.md")]
    public void DiagramDirectoryLinksResolveToDocumentationRoot(string sourcePath, string link, string rootPath)
    {
        var home = Source("# Home") with { Path = rootPath };
        var page = Source($"```mermaid\nflowchart LR\nA --> B\nclick A href \"{link}#home\"\n```") with { Path = sourcePath };
        var renderer = new ConfluenceStorageRenderer(Target, [home, page]);
        var result = renderer.ResolvePageLinks(renderer.Render(page), new Dictionary<string, string> { [home.Path] = "123", [page.Path] = "456" });
        var svg = XElement.Parse(System.Text.Encoding.UTF8.GetString(Assert.Single(result.Assets).Content));
        var href = svg.Descendants().Attributes("href").Single().Value;
        Assert.StartsWith("https://wiki.example.test/confluence/pages/viewpage.action?pageId=123#", href);
        Assert.EndsWith("-home", href);
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
        var renderer = new ConfluenceStorageRenderer(Target, [page]);
        var result = renderer.ResolvePageLinks(renderer.Render(page), new Dictionary<string, string> { [page.Path] = "123" });
        var svg = XElement.Parse(System.Text.Encoding.UTF8.GetString(Assert.Single(result.Assets).Content));
        var href = svg.Descendants().Attributes("href").Single().Value;
        Assert.StartsWith("https://wiki.example.test/confluence/pages/viewpage.action?pageId=123#", href);
        Assert.EndsWith("-home", href);
        Assert.Contains(Uri.EscapeDataString(Target.PageTitle(page).Replace(" ", "")), href);
    }

    [Fact]
    public void DiagramPageIdBindingRequiresKnownValidIdsAndRehashesAttachmentReferences()
    {
        var page = Source("```mermaid\nflowchart LR\nA --> B\nclick A href \"#home\"\n```");
        var renderer = new ConfluenceStorageRenderer(Target, [page]);
        var prepared = renderer.Render(page);
        Assert.Throws<InvalidOperationException>(() => renderer.ResolvePageLinks(prepared, new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => renderer.ResolvePageLinks(prepared, new Dictionary<string, string> { [page.Path] = "../OTHER" }));
        var resolved = renderer.ResolvePageLinks(prepared, new Dictionary<string, string> { [page.Path] = "123" });
        var asset = Assert.Single(resolved.Assets);
        Assert.NotEqual(Assert.Single(prepared.Assets).Name, asset.Name);
        Assert.Equal("mdu-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(asset.Content)) + ".svg", asset.Name);
        XNamespace ri = "http://atlassian.com/resource/identifier";
        Assert.Equal(asset.Name, Parse(resolved.Storage).Descendants(ri + "attachment").Single().Attribute(ri + "filename")?.Value);
        Assert.Equal(resolved, renderer.ResolvePageLinks(resolved, new Dictionary<string, string> { [page.Path] = "123" }));
    }

    [Fact]
    public void PageIdBindingDoesNotParseOrRewriteSourceAttachments()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mdu-confluence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string content = "An attachment, not generated SVG XML: mdu-confluence-page:README.md";
            File.WriteAllText(Path.Combine(directory, "download.svg"), content);
            var page = Source("[Download](download.svg)") with { FilePath = Path.Combine(directory, "README.md"), SourceRoot = directory };
            var renderer = new ConfluenceStorageRenderer(Target, [page]);
            var prepared = renderer.Render(page);
            var resolved = renderer.ResolvePageLinks(prepared, new Dictionary<string, string> { [page.Path] = "123" });
            Assert.Equal(prepared, resolved);
            Assert.Equal(content, System.Text.Encoding.UTF8.GetString(Assert.Single(resolved.Assets).Content));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ConfluenceSource Source(string markdown) => new("README.md", null, "Home", markdown, "/docs/README.md", "/docs");
    private static XElement Parse(string storage) =>
        XElement.Parse("<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" + storage + "</root>");
}
