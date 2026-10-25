using System.Text.Json;
using Markdown.Renderer;
using Markdown.Search;

namespace Mdu.Test;

public class SearchIndexBuilderTest
{
    [Theory]
    [InlineData("mermaid", "flowchart LR\nSource([Markdown]) --> HTML")]
    [InlineData("plantuml", "@startuml\ncomponent Renderer\n@enduml")]
    [InlineData("context", "title: Architecture\nsystems: []")]
    [InlineData("hero", "title: Welcome\nactions:\n  - text: Get started\n    link: Setup/README.md")]
    [InlineData("icon-search", "sets: material")]
    [InlineData("bash", "mdu render ./Docs ./site")]
    [InlineData("", "[Download the PDF](mdu:pdf)")]
    public void ExcludesFencedBlocksAndKeepsSurroundingProse(string language, string source)
    {
        var entries = BuildIndex($"# Guide\n\nBefore the block.\n\n```{language}\n{source}\n```\n\nAfter the block.");

        var entry = Assert.Single(entries);
        Assert.Equal("Before the block. After the block.", entry.GetProperty("text").GetString());
        Assert.Equal("guide.html#guide", entry.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("mermaid", "flowchart LR\nSource([Markdown]) --> HTML")]
    [InlineData("plantuml", "@startuml\ncomponent Renderer\n@enduml")]
    [InlineData("puml", "@startuml\ncomponent Renderer\n@enduml")]
    [InlineData("context", "System[Service] --> User[Person]")]
    [InlineData("isometric", "isometric diagram source")]
    [InlineData("hero", "title: Welcome\nactions:\n  - text: Get started\n    link: Setup/README.md")]
    [InlineData("icon-search", "sets: material")]
    public void ExcludesRendererBlockSourcesFromSearchableCode(string language, string source)
    {
        var entry = Assert.Single(BuildIndex($"# Guide\n\nProse before the block.\n\n```{language}\n{source}\n```"));

        Assert.Equal("Prose before the block.", entry.GetProperty("text").GetString());
        Assert.Equal(string.Empty, entry.GetProperty("code").GetString());
    }

    [Fact]
    public void ExcludesIndentedAndNestedBlocksWithoutLosingSections()
    {
        var entries = BuildIndex("""
            Introduction.

                mdu render ./Docs

            # Guide

            > Quoted prose.
            >
            > ~~~mermaid
            > flowchart LR
            > A --> B
            > ~~~

            - List prose.

              ```hero
              title: Hidden options
              ```

            ## Next

            Searchable explanation.
            """);

        Assert.Equal(3, entries.Length);
        Assert.Equal("Introduction.", entries[0].GetProperty("text").GetString());
        Assert.Equal("guide.html", entries[0].GetProperty("url").GetString());
        Assert.Equal("Quoted prose. List prose.", entries[1].GetProperty("text").GetString());
        Assert.Equal("Searchable explanation.", entries[2].GetProperty("text").GetString());
        Assert.Equal("guide.html#next", entries[2].GetProperty("url").GetString());
    }

    [Fact]
    public void KeepsLinkLabelsAndInlineCommandsWithoutMarkdownLinkSyntax()
    {
        var entries = BuildIndex("""
            # Guide

            Use `mdu render` and [Download the PDF](mdu:pdf).
            Write `[Download the PDF](mdu:pdf)` or `[**Setup**](Setup/README.md "Install")`.
            See [the guide][guide] and [details](https://example.com/docs_(latest)).
            Keep `--pattern <glob>`, `items[0]`, and `a * b`.

            [guide]: Setup/README.md
            """);

        Assert.Equal("Use mdu render and Download the PDF . Write Download the PDF or Setup . " +
                     "See the guide and details . Keep --pattern <glob> , items[0] , and a * b .",
            Assert.Single(entries).GetProperty("text").GetString());
    }

    [Fact]
    public void KeepsProseOnPagesWithoutHeadingsAndIgnoresScripts()
    {
        var entries = BuildIndex("""
            Searchable **prose** with &amp; entities.

            ```bash
            command --options
            ```

            <script>hiddenScript()</script>
            <style>.hidden { color: red; }</style>
            """);

        var entry = Assert.Single(entries);
        Assert.Equal("Searchable prose with & entities.", entry.GetProperty("text").GetString());
        Assert.Equal("guide.html", entry.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("handlers[name](event)")]
    [InlineData("array[index](argument)")]
    public void PreservesInlineCodeThatResemblesLinks(string code)
    {
        var entry = Assert.Single(BuildIndex($"# Guide\n\nCall `{code}`."));

        Assert.Equal($"Call {code} .", entry.GetProperty("text").GetString());
        Assert.Equal(code, entry.GetProperty("code").GetString());
    }

    [Fact]
    public void ReplacesOnlyDocumentedLinksAndKeepsTheirSyntaxSearchable()
    {
        var entry = Assert.Single(BuildIndex("""
            # Guide

            Write `- [Download the PDF](mdu:pdf) <placeholder> \\*literal\\*`.
            """));

        Assert.Equal(@"Write - Download the PDF <placeholder> \\*literal\\* .", entry.GetProperty("text").GetString());
        Assert.Equal(@"- [Download the PDF](mdu:pdf) <placeholder> \\*literal\\*", entry.GetProperty("code").GetString());
    }

    [Fact]
    public void IndexesCodeInItsOwnSectionWithoutAddingItToSnippets()
    {
        var entries = BuildIndex("""
            ```sh
            intro-command
            ```

            # Install

            Installation instructions.

            ```bash
            package-manager --install unique-package
            ```

            ## Configure

                config-tool --set unique-value
            """);

        Assert.Equal(3, entries.Length);
        Assert.Equal("", entries[0].GetProperty("text").GetString());
        Assert.Equal("intro-command", entries[0].GetProperty("code").GetString());
        Assert.Equal("guide.html", entries[0].GetProperty("url").GetString());
        Assert.Equal("Installation instructions.", entries[1].GetProperty("text").GetString());
        Assert.Equal("package-manager --install unique-package", entries[1].GetProperty("code").GetString());
        Assert.Equal("guide.html#install", entries[1].GetProperty("url").GetString());
        Assert.Equal("", entries[2].GetProperty("text").GetString());
        Assert.Equal("config-tool --set unique-value", entries[2].GetProperty("code").GetString());
        Assert.Equal("guide.html#configure", entries[2].GetProperty("url").GetString());
    }

    [Fact]
    public void IndexesDisplayMathOutsideTheSnippetText()
    {
        var entry = Assert.Single(BuildIndex("""
            # Formula

            $$
            E = mc^2
            $$
            """));

        Assert.Equal("", entry.GetProperty("text").GetString());
        Assert.Contains("E = mc^2", entry.GetProperty("code").GetString());
    }

    [Fact]
    public void IndexesExpandedOpenApiDescriptionsAndNotSpecificationOptions()
    {
        const string markdown = """
            # API guide

            API overview prose.

            ```openapi
            openapi: 3.1.0
            info:
              title: Inline API
              version: 1.0.0
            paths:
              /widgets:
                get:
                  summary: Find widgets
                  description: Searchable endpoint explanation.
                  responses:
                    '200':
                      description: Widget list
            ```
            """;

        var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            markdown,
            Path.Combine(Path.GetTempPath(), "search-openapi.md"),
            includeSpecificationPageLink: false);
        var entries = BuildIndex(expanded);
        var apiEntry = entries.Single(entry =>
            entry.GetProperty("section").GetString() == "API overview");

        Assert.Contains(entries, entry => entry.GetProperty("text").GetString()!.Contains("Find widgets", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.GetProperty("text").GetString()!.Contains("Searchable endpoint explanation", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.GetProperty("text").GetString()!.Contains("openapi: 3.1.0", StringComparison.Ordinal));
        Assert.Contains("Inline API", apiEntry.GetProperty("text").GetString());
    }

    [Fact]
    public void IndexesEachOpenApiOperationOnceWithItsSections()
    {
        const string markdown = """
            # API guide

            ```openapi
            openapi: 3.1.0
            info:
              title: Inline API
              version: 1.0.0
            paths:
              /widgets/{widget_id}:
                get:
                  summary: Read widget
                  parameters:
                    - in: path
                      name: widget_id
                      required: true
                      description: Widget reservation key.
                      schema: { type: string }
                  responses:
                    '200':
                      description: Widget details
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              name: { type: string }
              /gadgets:
                get:
                  summary: List gadgets
                  responses:
                    '200':
                      description: Gadget list
            ```

            ## Next steps

            Continue reading.
            """;

        var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            markdown,
            Path.Combine(Path.GetTempPath(), "search-openapi-sections.md"),
            includeSpecificationPageLink: false);
        var entries = BuildIndex(expanded);
        var hits = entries.Where(entry => entry.GetProperty("text").GetString()!
            .Contains("reservation key", StringComparison.Ordinal)).ToArray();

        var widget = Assert.Single(hits);
        Assert.StartsWith("GET /widgets/", widget.GetProperty("section").GetString());
        Assert.Contains("Widget details", widget.GetProperty("text").GetString());
        Assert.DoesNotContain(entries, entry => entry.GetProperty("section").GetString() is "Path Parameters" or "Schema" or "Example");
        Assert.Contains(entries, entry => entry.GetProperty("section").GetString()!.StartsWith("GET /gadgets", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.GetProperty("section").GetString() == "Next steps");
    }

    private static JsonElement[] BuildIndex(string markdown)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mdu-search-test-{Guid.NewGuid():N}");
        try
        {
            var builder = new SearchIndexBuilder();
            builder.AddPage("Guide", "guide.html", markdown);
            builder.WriteIndex(outputPath);
            using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "search-index.json")));
            return index.RootElement.EnumerateArray().Select(entry => entry.Clone()).ToArray();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }
}
