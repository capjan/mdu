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

    [Fact]
    public void KeepsDescriptionHeadingsInTheOperationAndHeadingsAfterTheBlockSeparate()
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
                  description: "### Notes\n\nImplementation details."
                  parameters:
                    - in: path
                      name: widget_id
                      required: true
                      description: Widget reservation key.
                      schema: { type: string }
                  responses:
                    '200':
                      description: OK
            ```

            #### Troubleshooting

            Check token-xyz.
            """;

        var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            markdown,
            Path.Combine(Path.GetTempPath(), "search-openapi-boundaries.md"),
            includeSpecificationPageLink: false);
        var entries = BuildIndex(expanded);

        var widget = Assert.Single(entries, entry => entry.GetProperty("section").GetString()!.StartsWith("GET /widgets/", StringComparison.Ordinal));
        Assert.Contains("Implementation details.", widget.GetProperty("text").GetString());
        Assert.Contains("Widget reservation key.", widget.GetProperty("text").GetString());
        Assert.DoesNotContain(entries, entry => entry.GetProperty("section").GetString() is "Notes" or "Path Parameters");
        var troubleshooting = Assert.Single(entries, entry => entry.GetProperty("section").GetString() == "Troubleshooting");
        Assert.Equal("guide.html#troubleshooting", troubleshooting.GetProperty("url").GetString());
        Assert.Contains("token-xyz", troubleshooting.GetProperty("text").GetString());
        Assert.DoesNotContain("token-xyz", widget.GetProperty("text").GetString());
    }

    [Fact]
    public void KeepsProseAfterAnOpenApiBlockInTheSectionAroundTheBlock()
    {
        const string markdown = """
            # API guide

            Intro.

            ```openapi
            openapi: 3.1.0
            info:
              title: Inline API
              version: 1.0.0
            paths:
              /gadgets:
                get:
                  summary: List gadgets
                  responses:
                    '200':
                      description: Gadget list
            ```

            Post-block paragraph with `post-token`.

            ## Next steps

            Continue reading.
            """;

        var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            markdown,
            Path.Combine(Path.GetTempPath(), "search-openapi-after-block.md"),
            includeSpecificationPageLink: false);
        var entries = BuildIndex(expanded);

        var gadgets = Assert.Single(entries, entry => entry.GetProperty("section").GetString()!.StartsWith("GET /gadgets", StringComparison.Ordinal));
        Assert.DoesNotContain("Post-block paragraph", gadgets.GetProperty("text").GetString());
        Assert.DoesNotContain("post-token", gadgets.GetProperty("code").GetString());
        var guide = Assert.Single(entries, entry => entry.GetProperty("url").GetString() == "guide.html#api-guide");
        Assert.Contains("Post-block paragraph", guide.GetProperty("text").GetString());
        Assert.Contains("post-token", guide.GetProperty("code").GetString());
    }

    [Fact]
    public void KeepsTwoOpenApiBlocksOnOnePageApart()
    {
        const string markdown = """
            # API guide

            ```openapi
            openapi: 3.1.0
            info: { title: First API, version: 1.0.0 }
            paths:
              /v1/widgets:
                get:
                  summary: List widgets
                  responses:
                    '200': { description: Widget list }
            ```

            Between the references.

            ```openapi
            openapi: 3.1.0
            info: { title: Second API, version: 1.0.0 }
            paths:
              /v2/gadgets:
                get:
                  summary: List gadgets
                  responses:
                    '200': { description: Gadget list }
            ```
            """;

        var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            markdown,
            Path.Combine(Path.GetTempPath(), "search-openapi-two-blocks.md"),
            includeSpecificationPageLink: false);
        var entries = BuildIndex(expanded);

        var widgets = Assert.Single(entries, entry => entry.GetProperty("section").GetString()!.StartsWith("GET /v1/widgets", StringComparison.Ordinal));
        var gadgets = Assert.Single(entries, entry => entry.GetProperty("section").GetString()!.StartsWith("GET /v2/gadgets", StringComparison.Ordinal));
        Assert.DoesNotContain("Between the references", widgets.GetProperty("text").GetString());
        Assert.DoesNotContain("Gadget list", widgets.GetProperty("text").GetString());
        Assert.DoesNotContain("Widget list", gadgets.GetProperty("text").GetString());
        Assert.Contains(entries, entry => entry.GetProperty("text").GetString()!.Contains("Between the references", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsHtmlHeadingsAndExcludesHeadingsInsideNonRenderedContent()
    {
        var entries = BuildIndex("""
            <div>
            <h1 data-label="a > b" id='visible'>Visible <em>heading</em></h1>
            <p>Before <strong>nested</strong> text.</p>
            <template><h2 id="hidden">Hidden heading</h2><p>Hidden template.</p></template>
            <script>const example = '<h2 id="fake">Fake heading</h2>';</script>
            <style>/* <h2 id="style">Hidden style</h2> */</style>
            <!-- <h2 id="comment">Hidden comment</h2> -->
            <h2 ID=next>Next &amp; final</h2>
            <p>After.</p>
            </div>
            """);

        Assert.Equal(2, entries.Length);
        Assert.Equal("Visible heading", entries[0].GetProperty("section").GetString());
        Assert.Equal("guide.html#visible", entries[0].GetProperty("url").GetString());
        Assert.Equal("Before nested text.", entries[0].GetProperty("text").GetString());
        Assert.Equal("Next & final", entries[1].GetProperty("section").GetString());
        Assert.Equal("guide.html#next", entries[1].GetProperty("url").GetString());
        Assert.Equal("After.", entries[1].GetProperty("text").GetString());
    }

    [Fact]
    public void PreservesPlainMarkdownTextBoundariesAndHeadingIds()
    {
        var entries = BuildIndex("""
            Introduction &amp; details.

            # 6. Guide

            Read **nested *text*** and [the guide](next.md), then `command`.

            > Quoted paragraph.

            - First
            - Second

            ## Custom {#specific}

            Final.
            """);

        Assert.Equal(3, entries.Length);
        Assert.Equal("Introduction & details.", entries[0].GetProperty("text").GetString());
        Assert.Equal("guide.html#6-guide", entries[1].GetProperty("url").GetString());
        Assert.Equal("Read nested text and the guide , then command . Quoted paragraph. First Second",
            entries[1].GetProperty("text").GetString());
        Assert.Equal("guide.html#specific", entries[2].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData(
        "# Guide\n\n| Name | Value |\n| --- | --- |\n| **A** | `b` |\n| [C](next.md) | D |",
        "Guide", "Name Value A b C D", "guide.html#guide", "b")]
    [InlineData(
        "# Guide\n\n+---+---+\n| A | B |\n+===+===+\n| C | D |\n+---+---+",
        "Guide", "A B C D", "guide.html#guide", "")]
    [InlineData(
        "# Guide\n\n> [!NOTE]\n> Useful **text**.\n\n> [!TIP]\n> More.\n\n> [!IMPORTANT]\n> Read.\n\n> [!WARNING]\n> Careful.\n\n> [!CAUTION]\n> Stop.",
        "Guide", "Note Useful text . Tip More. Important Read. Warning Careful. Caution Stop.", "guide.html#guide", "")]
    [InlineData(
        "# Guide\n\n> [!note]\n> Lowercase.\n\n> [!unknown]\n> Ordinary quote.",
        "Guide", "Note Lowercase. [!unknown] Ordinary quote.", "guide.html#guide", "")]
    [InlineData(
        "# ++cmd+s++ Guide :rocket:\n\nUse ++ctrl+alt+del++ or ++arrow-up++ and ++cmd+\"A &amp; B\"++. :rocket: End.",
        "⌘ Cmd + S Guide 🚀", "Use ⌃ Ctrl + Alt + ⌦ Del or ↑ Up and ⌘ Cmd + A & B . 🚀 End.", "guide.html#guide", "")]
    [InlineData(
        "# Guide\n\n- [x] Done\n- [ ] Pending **task**\n\n![**Alt** with `code`](image.png) End.",
        "Guide", "Done Pending task End.", "guide.html#guide", "code")]
    [InlineData(
        "# Guide\n\n| Shortcut | Description |\n| --- | --- |\n| ++cmd+s++ | :rocket: **Go** |\n\n> [!TIP]\n> Use ++arrow-down++.",
        "Guide", "Shortcut Description ⌘ Cmd + S 🚀 Go Tip Use ↓ Down .", "guide.html#guide", "")]
    [InlineData(
        "# Footnotes\n\nFirst[^b]. Again[^b]. Second[^a].\n\n[^a]: Alpha.\n[^b]: Beta **text**.",
        "Footnotes", "First 1 . Again 1 . Second 2 . Beta text . ↩ ↩ Alpha. ↩", "guide.html#footnotes", "")]
    [InlineData(
        "# Notes\n\nText[^n].\n\n[^n]: First paragraph.\n\n    Second with `code`.\n\n[^unused]: Not referenced.",
        "Notes", "Text 1 . First paragraph. Second with code . ↩", "guide.html#notes", "code")]
    [InlineData(
        "# Formula\n\nThe area is $A = \\pi r^2$.",
        "Formula", "The area is \\(A = \\pi r^2\\) .", "guide.html#formula", "")]
    [InlineData(
        "# Formula $x$\n\nInline $$x + y$$ and $a & b$.",
        "Formula \\(x\\)", "Inline \\(x + y\\) and \\(a & b\\) .", "guide.html#formula", "")]
    [InlineData(
        "# Icons :material-home:\n\nUse :material-home: and :octicons-heart-fill-24: :dashboard-github:.",
        "Icons", "Use and .", "guide.html#icons", "")]
    public void PreservesExtensionTextAndAnchors(string markdown, string section, string text, string url, string code)
    {
        var entry = Assert.Single(BuildIndex(markdown));

        Assert.Equal(section, entry.GetProperty("section").GetString());
        Assert.Equal(text, entry.GetProperty("text").GetString());
        Assert.Equal(url, entry.GetProperty("url").GetString());
        Assert.Equal(code, entry.GetProperty("code").GetString());
    }

    [Fact]
    public void KeepsFootnoteReferencesInHeadingsAndDefinitionsInTheFinalSection()
    {
        var entries = BuildIndex("# Heading[^n]\n\n## Next\n\n[^n]: Note.");

        Assert.Equal(2, entries.Length);
        Assert.Equal("Heading 1", entries[0].GetProperty("section").GetString());
        Assert.Equal("guide.html#heading", entries[0].GetProperty("url").GetString());
        Assert.Equal(string.Empty, entries[0].GetProperty("text").GetString());
        Assert.Equal("Note. ↩", entries[1].GetProperty("text").GetString());
        Assert.Equal("guide.html#next", entries[1].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("<h4>Notes</h4>")]
    [InlineData("<h4><select><option>Notes</option></select></h4>")]
    public void DoesNotAssignIntroductionCodeToAnOpenApiHeadingWithoutAnId(string heading)
    {
        var entries = BuildIndex($$"""
            ```sh
            intro-only-command
            ```

            # API guide

            <!--mdu-openapi-start-->

            ### `GET` /widgets {.openapi-operation}

            {{heading}}
            <p>Operation details.</p>

            <!--mdu-openapi-end-->
            """);

        Assert.Equal("intro-only-command", entries[0].GetProperty("code").GetString());
        var operation = Assert.Single(entries, entry => entry.GetProperty("section").GetString() == "GET /widgets");
        Assert.Contains("Notes", operation.GetProperty("text").GetString());
        Assert.Equal("GET", operation.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("<h2>Limits</h2>")]
    [InlineData("<H2>Limits</H2>")]
    public void KeepsTextUnderHtmlHeadingsWithoutIds(string heading)
    {
        var entries = BuildIndex($"# Guide\n\n{heading}\n\n<p>Rate limit is 100.</p>");
        var entry = Assert.Single(entries);
        Assert.Equal("guide.html#guide", entry.GetProperty("url").GetString());
        Assert.Equal("Limits Rate limit is 100.", entry.GetProperty("text").GetString());
    }

    [Fact]
    public void IndexesPagesContainingOnlyHtmlHeadingsWithoutIds()
    {
        var entry = Assert.Single(BuildIndex("<h2>Limits</h2><p>Rate limit is 100.</p>"));
        Assert.Equal("guide.html", entry.GetProperty("url").GetString());
        Assert.Equal("Limits Rate limit is 100.", entry.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("iframe")]
    [InlineData("noembed")]
    [InlineData("noframes")]
    [InlineData("noscript")]
    public void ExcludesNonRenderedFallbackMarkup(string element)
    {
        var entry = Assert.Single(BuildIndex($"# Guide\n\nBefore.\n\n<{element}><h2 id='hidden'>Hidden</h2><p>Your browser <a href='y'>fallback</a></p></{element}>\n\nAfter."));
        Assert.Equal("Before. After.", entry.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("<h2 id='setup'>Setup</h2>")]
    [InlineData("<h2 id='setup'><select><option>Setup</option></select></h2>")]
    public void AssignsCodeToHtmlHeadingsInDocumentOrder(string heading)
    {
        var entries = BuildIndex($"# Guide\n\n```sh\nintro-tool\n```\n\n{heading}\n\nUse `inline-tool`.\n\n```sh\ninstall-tool\n```\n\n## Next\n\n```sh\nnext-tool\n```");
        Assert.Equal(3, entries.Length);
        Assert.Equal("intro-tool", entries[0].GetProperty("code").GetString());
        Assert.Equal("guide.html#setup", entries[1].GetProperty("url").GetString());
        Assert.Equal("inline-tool install-tool", entries[1].GetProperty("code").GetString());
        Assert.Equal("next-tool", entries[2].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("<!--mdu-openapi-start-->")]
    [InlineData("<!--mdu-openapi-end-->")]
    public void KeepsMarkerOnlyPagesSearchable(string marker)
    {
        var entry = Assert.Single(BuildIndex(marker));
        Assert.Equal("Guide", entry.GetProperty("title").GetString());
        Assert.Equal("guide.html", entry.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("<!--mdu-openapi-start-->")]
    [InlineData("<!--mdu-openapi-end-->")]
    public void IgnoresUnpairedOpenApiMarkers(string marker)
    {
        var entries = BuildIndex($$"""
            ### `GET` /widgets {.openapi-operation}

            First description.

            {{marker}}

            #### Notes

            Extra details.
            """);
        var entry = Assert.Single(entries);
        Assert.Contains("Notes Extra details.", entry.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("foo<!-- note -->bar")]
    [InlineData("foo<script>hidden</script>bar")]
    [InlineData("foo<style>hidden</style>bar")]
    [InlineData("foo<template>hidden</template>bar")]
    [InlineData("foo<noscript>hidden</noscript>bar")]
    public void KeepsWordBoundariesAroundExcludedContent(string html)
    {
        var entry = Assert.Single(BuildIndex($"# Guide\n\n<p>{html}</p>"));
        Assert.Equal("foo bar", entry.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("xmp")]
    [InlineData("title")]
    public void DoesNotExposeInternalCodeMarkersInRawText(string element)
    {
        var entry = Assert.Single(BuildIndex($"# Guide\n\n<{element}>\n\n```sh\ninstall-tool\n```\n\n</{element}>"));
        var text = entry.GetProperty("text").GetString()!;
        var code = entry.GetProperty("code").GetString()!;
        Assert.DoesNotContain("mdu-search-code-", text);
        Assert.Contains("install-tool", text + code);
    }

    [Fact]
    public void DoesNotIndexCodeInsideTemplates()
    {
        var entry = Assert.Single(BuildIndex("# Guide\n\n<template>\n\n```sh\nhidden-command\n```\n\n</template>\n\nVisible."));
        Assert.Equal("Visible.", entry.GetProperty("text").GetString());
        Assert.Equal(string.Empty, entry.GetProperty("code").GetString());
    }

    [Fact]
    public void KeepsPageLevelProseAroundAnOpenApiBlockWithoutAnOuterHeading()
    {
        var entries = BuildIndex("""
            <!--mdu-openapi-start-->

            Before the operation.

            ### `GET` /widgets {.openapi-operation}

            Operation details.

            <!--mdu-openapi-end-->

            After the reference.
            """);

        Assert.Equal(2, entries.Length);
        var page = Assert.Single(entries, entry => entry.GetProperty("url").GetString() == "guide.html");
        Assert.Equal("Before the operation. After the reference.", page.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("openapi-tag", "Users")]
    [InlineData("openapi-overview", "Users")]
    [InlineData("openapi-tag", "<select><option>Users</option></select>")]
    [InlineData("openapi-overview", "<select><option>Users</option></select>")]
    public void EndsOperationGroupingAtApiHeadingsWithoutIds(string sectionClass, string heading)
    {
        var entries = BuildIndex($$"""
            <!--mdu-openapi-start-->

            <h3 id="operation" class="openapi-operation">GET /widgets</h3>
            <p>Operation details.</p>
            <h2 class="{{sectionClass}}">{{heading}}</h2>
            <h3 id="users-notes">Notes</h3>
            <p>User details.</p>

            ```sh
            user-command
            ```

            <!--mdu-openapi-end-->
            """);

        Assert.Equal(2, entries.Length);
        Assert.Equal("guide.html#operation", entries[0].GetProperty("url").GetString());
        Assert.Equal("Operation details. Users", entries[0].GetProperty("text").GetString());
        Assert.Equal(string.Empty, entries[0].GetProperty("code").GetString());
        Assert.Equal("guide.html#users-notes", entries[1].GetProperty("url").GetString());
        Assert.Equal("User details.", entries[1].GetProperty("text").GetString());
        Assert.Equal("user-command", entries[1].GetProperty("code").GetString());
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
