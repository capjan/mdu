using Builder = Markdown.Search.SearchIndexBuilder;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;
using Xunit;

namespace Mdu.Test;

public class SearchHtmlReaderTest
{
    private static string Snapshot(IEnumerable<Builder.SearchPart> parts) => JsonSerializer.Serialize(parts.Select(part => new
    {
        part.Id, part.Title, part.IsOperation, part.IsApiSection, part.Marker,
        Text = Regex.Replace(part.Text.ToString(), @"\s+", " ").Trim(),
        part.Code
    }));

    private static bool TryRead(string html, out List<Builder.SearchPart> parts) =>
        Builder.TryReadTokens(html, svgContext: false, out parts);

    [Theory]
    [InlineData("<H2 data-label='a > b' ID='a&amp;b'>Nested <strong>heading</strong></H2><p>Text &amp; entities.</p>")]
    [InlineData("<h2 id=operation class='extra\topenapi-operation\nother'>GET <code>/widgets</code></h2><p>Summary</p>")]
    [InlineData("<!--mdu-openapi-start--><h2 id='overview' class='openapi-overview'>API</h2><h3 id='op' class='openapi-operation'>GET /</h3><p>Operation</p><!--mdu-openapi-end--><p>After</p>")]
    [InlineData("<div><details><summary>More</summary><h2 id='details'>Inside</h2><p>Details.</p></details></div>")]
    [InlineData("<table><thead><tr><th>Name</th></tr></thead><tbody><tr><td><h2 id='cell'>Heading</h2><p>Cell.</p></td></tr></tbody></table>")]
    [InlineData("<p>SVG <svg viewBox='0 0 10 10'><g><path d='M0 0'/><text>Label <tspan>nested</tspan></text></g></svg> end.</p>")]
    [InlineData("<p>SVG <svg><text><![CDATA[A < B]]></text><title>Title &amp; text</title></svg> end.</p>")]
    [InlineData("<p>Before<!-- hidden <h2 id='fake'>heading</h2> -->after<br>next<img src='a.png' alt='Hidden alt'></p>")]
    [InlineData("<template><h2 id='hidden'>Hidden</h2></template><h2 id='visible'>Visible</h2>")]
    [InlineData("<script>const html = '<h2 id=hidden>Hidden</h2>';</script><p>Visible</p>")]
    [InlineData("<style>/* <h2 id='hidden'>Hidden</h2> */</style>")]
    [InlineData("<textarea><h2 id='literal'>Literal markup</h2></textarea>")]
    [InlineData("<video controls><source src='film.mp4' type='video/mp4'><track src='captions.vtt'>Fallback <strong>text</strong>.</video>")]
    [InlineData("<p>Before<audio><source src='audio.mp3'>Fallback.</audio>after.</p>")]
    [InlineData("<p>Before<script>const sample = '<h2 id=hidden>Hidden</h2>';</script>after.</p>")]
    [InlineData("<h2 id='heading'>Before<style>.x { content: '<h3 id=hidden>Hidden</h3>' }</style>after.</h2>")]
    [InlineData("<script><!--<script>double escaped</script>still script--></script><h2 id='visible'>Visible</h2>")]
    [InlineData("<template><!--mdu-openapi-start--><h2 id='hidden'>Hidden</h2><template>Nested</template><script>let example = '</template>';</script></template><h2 id='visible'>Visible</h2>")]
    [InlineData("<h2 id='heading'>Before<template><h3 id='hidden'>Hidden</h3></template>after</h2>")]
    [InlineData("<textarea>&amp; &lt;h2&gt; <!--mdu-openapi-start--> <h2 id='literal'>Literal</h2></textarea><h2 id='visible'>Visible</h2>")]
    [InlineData("<svg><defs><linearGradient id='g'><stop offset='0' stop-color='red'/></linearGradient><clipPath id='c'><rect width='10' height='10'/></clipPath></defs><style><![CDATA[text { fill: url(#g) } /* <h2>hidden</h2> */]]></style><text>SVG <tspan>label</tspan></text></svg>")]
    public void AcceptedHtmlMatchesFullDom(string html)
    {
        Assert.True(TryRead(html, out var parts));
        Assert.Equal(Snapshot(Builder.ReadHtmlDom(html)), Snapshot(parts!));
    }

    [Theory]
    [InlineData("<select><h2 id='ignored'>Ignored tag</h2><option>Choice</option></select>")]
    [InlineData("<table>Fostered text<div><h2 id='outside'>Moved</h2></div></table>")]
    [InlineData("<table><tr><td>Implicit tbody</td></tr></table>")]
    [InlineData("<p>Implicit close<h2 id='next'>Next</h2></p>")]
    [InlineData("<p><strong>Misnested</p></strong>")]
    [InlineData("<a href='one'><a href='two'>Nested anchors</a></a>")]
    [InlineData("<div/><h2 id='next'>Non-void self-closing</h2>")]
    [InlineData("<p>Incomplete")]
    [InlineData("<p title='unterminated>Text</p>")]
    [InlineData("<!-- incomplete comment")]
    [InlineData("<h2 id='duplicate' ID='other'>Duplicate attribute</h2>")]
    [InlineData("<!doctype html><html><body><h2 id='next'>Document</h2></body></html>")]
    [InlineData("<svg><desc><text><title><g>Literal tags</g></title></text></desc></svg>")]
    [InlineData("<svg><title><svg><text>Integration point</text></svg></title></svg>")]
    [InlineData("<svg><script>let text = '<script>nested</script>';</script></svg>")]
    [InlineData("<svg><style><text>Not a style text node</text></style></svg>")]
    public void AmbiguousHtmlUsesFullDom(string html)
    {
        Assert.False(TryRead(html, out _));
        Assert.Equal(Snapshot(Builder.ReadHtmlDom(html)), Snapshot(new Builder().ReadHtml(html)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<path d='M0 0L1 1'/>")]
    [InlineData("<text>SVG <tspan>label</tspan></text>")]
    [InlineData("<style><![CDATA[text { fill: red }]]></style><text>Label</text>")]
    public void ReadsSvgFragmentsWithoutCopyingThemIntoAWrapper(string svg)
    {
        Assert.True(Builder.TryReadTokens(svg, svgContext: true, out var parts));
        Assert.Equal(Snapshot(Builder.ReadHtmlDom($"<svg>{svg}</svg>")), Snapshot(parts));
    }

    [Fact]
    public void SvgFragmentsCannotCloseTheirParsingContext()
    {
        Assert.False(Builder.TryReadTokens("</svg><h2 id='outside'>Outside</h2>", svgContext: true, out _));
    }

    [Fact]
    public void SupportedNestedStructuresMatchFullDom()
    {
        string[] tags = ["div", "p", "span", "a", "strong", "h2", "table", "tbody", "tr", "td", "ul", "li", "svg", "g", "text", "title", "summary", "details"];
        var accepted = 0;
        foreach (var outer in tags)
        foreach (var middle in tags)
        foreach (var inner in tags)
        {
            var html = $"<{outer} id='outer'>Lead<{middle} id='middle'>Middle<{inner} id='inner'>Inner</{inner}>Tail</{middle}>End</{outer}><h2 id='next'>Next</h2><p>Final.</p>";
            if (!TryRead(html, out var parts)) continue;
            accepted++;
            Assert.True(Snapshot(Builder.ReadHtmlDom(html)) == Snapshot(parts!), html);
        }
        Assert.True(accepted > 100);
    }

    [Fact]
    public void SpecialContextsMatchFullDom()
    {
        string[] tags = ["div", "p", "span", "h2", "template", "script", "style", "textarea", "svg", "g", "text", "title", "desc", "video"];
        foreach (var outer in tags)
        foreach (var middle in tags)
        foreach (var inner in tags)
        {
            var html = $"<h2 id='before'>Before</h2><{outer}>Lead<{middle}>Middle<{inner}>Inner</{inner}>Tail</{middle}>End</{outer}><h2 id='after'>After</h2><p>Final.</p>";
            Assert.True(Snapshot(Builder.ReadHtmlDom(html)) == Snapshot(new Builder().ReadHtml(html)), html);
        }
    }

    [Theory]
    [InlineData("# Guide\n\nParagraph **bold** with [link](other.md), `code` and &amp; entity.\n\n## Next\n\n- One\n- Two")]
    [InlineData("# Guide\n\n| Key | Value |\n| --- | --- |\n| `tool` | ++ctrl+c++ :rocket: |\n\n> [!TIP]\n> Useful **text**.")]
    [InlineData("# Guide\n\n- [x] Done\n- [ ] Next\n\n![Alt with `code`](image.png)")]
    [InlineData("# Guide\n\nBefore.\n\n```sh\ninstall-tool\n```\n\n```mermaid\nA --> B\n```\n\nAfter.")]
    [InlineData("# Footnotes\n\nFirst[^b]. Again[^b]. Second[^a].\n\n[^a]: Alpha.\n[^b]: Beta **text**.\n[^unused]: Unused.")]
    [InlineData("# Formula $x$\n\nUse $x + y$ and :material-home:.")]
    [InlineData("# Guide\n\nWrite `[**Setup**](next.md)` and `[link](next.md)`.")]
    [InlineData("# Guide\n\n[![Alt with `code`](image.png)](next.md)\n\n![Outer ![Inner `nested`](inner.png)](outer.png)")]
    public void MarkdownAndHtmlReadersAgree(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGridTables().UsePipeTables().UseAdvancedExtensions()
            .UseHeadingIds().UseGitHubAlerts().UseIconsAndEmojis().UseKeyboardKeys().Build();
        var reader = new Builder();
        var direct = reader.ReadMarkdown(Markdig.Markdown.Parse(markdown, pipeline));
        var html = reader.ReadRenderedHtml(Markdig.Markdown.Parse(markdown, pipeline), pipeline);
        Assert.Equal(Snapshot(direct), Snapshot(html));
    }

    [Fact]
    public void ReusesTheDomParserAcrossPages()
    {
        const string html = "<h2 id='guide'>Guide</h2><select><option>Choice</option></select>";
        var reader = new Builder();
        for (var index = 0; index < 3; index++)
            Assert.Equal(Snapshot(Builder.ReadHtmlDom(html)), Snapshot(reader.ReadHtml(html)));
    }

    [Fact]
    public void ExpandedOpenApiUsesTokenizerAndMatchesFullDom()
    {
        const string source = """
            # API guide

            ```openapi
            openapi: 3.1.0
            info: { title: Widgets, version: 1.0.0 }
            servers:
              - url: https://example.org/api
            security:
              - apiKey: []
            components:
              securitySchemes:
                apiKey: { type: apiKey, in: header, name: X-API-Key }
            paths:
              /widgets/{id}:
                get:
                  summary: Read a widget
                  deprecated: true
                  parameters:
                    - in: path
                      name: id
                      required: true
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
            ```
            """;
        var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(source,
            Path.Combine(Path.GetTempPath(), "search-html-openapi.md"), includeSpecificationPageLink: false);
        var pipeline = new MarkdownPipelineBuilder().UseGridTables().UsePipeTables().UseAdvancedExtensions()
            .UseHeadingIds().UseGitHubAlerts().UseIconsAndEmojis().UseKeyboardKeys().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        foreach (var block in document.Descendants().OfType<CodeBlock>().ToArray()) block.Parent!.Remove(block);
        var html = document.ToHtml(pipeline);
        Assert.True(TryRead(html, out var parts));
        Assert.Equal(Snapshot(Builder.ReadHtmlDom(html)), Snapshot(parts!));
    }
}
