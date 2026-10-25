using System.Text.Json;
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
    [InlineData("openapi", "spec: api/orders.yaml")]
    [InlineData("bash", "mdu render ./Docs ./site")]
    [InlineData("", "[Download the PDF](mdu:pdf)")]
    public void ExcludesFencedBlocksAndKeepsSurroundingProse(string language, string source)
    {
        var entries = BuildIndex($"# Guide\n\nBefore the block.\n\n```{language}\n{source}\n```\n\nAfter the block.");

        var entry = Assert.Single(entries);
        Assert.Equal("Before the block. After the block.", entry.GetProperty("text").GetString());
        Assert.Equal("guide.html#guide", entry.GetProperty("url").GetString());
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
