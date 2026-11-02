using System.Collections;
using System.Reflection;
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
    private static readonly Type Builder = typeof(Markdown.Search.SearchIndexBuilder);
    private static object Invoke(string name, params object?[] arguments) =>
        Builder.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    private static string Snapshot(object value) => JsonSerializer.Serialize(((IEnumerable)value).Cast<object>().Select(part =>
        new[] { "Id", "Title", "IsOperation", "IsApiSection", "Marker", "Text" }.Select(name =>
        {
            var text = part.GetType().GetProperty(name)!.GetValue(part)?.ToString();
            return name == "Text" && text is not null ? Regex.Replace(text, @"\s+", " ").Trim() : text;
        }).ToArray()).ToArray());

    private static bool TryRead(string html, out object? parts)
    {
        object?[] arguments = [html, null];
        var accepted = (bool)Invoke("TryReadHtmlTokens", arguments);
        parts = arguments[1];
        return accepted;
    }

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
        Assert.Equal(Snapshot(Invoke("ReadHtmlDom", html)), Snapshot(parts!));
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
        Assert.Equal(Snapshot(Invoke("ReadHtmlDom", html)), Snapshot(Invoke("ReadHtml", html)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<path d='M0 0L1 1'/>")]
    [InlineData("<text>SVG <tspan>label</tspan></text>")]
    [InlineData("<style><![CDATA[text { fill: red }]]></style><text>Label</text>")]
    public void ReadsSvgFragmentsWithoutCopyingThemIntoAWrapper(string svg)
    {
        object?[] arguments = [svg, true, null];
        Assert.True((bool)Invoke("TryReadTokens", arguments));
        Assert.Equal(Snapshot(Invoke("ReadHtmlDom", $"<svg>{svg}</svg>")), Snapshot(arguments[2]!));
    }

    [Fact]
    public void SvgFragmentsCannotCloseTheirParsingContext()
    {
        object?[] arguments = ["</svg><h2 id='outside'>Outside</h2>", true, null];
        Assert.False((bool)Invoke("TryReadTokens", arguments));
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
            Assert.True(Snapshot(Invoke("ReadHtmlDom", html)) == Snapshot(parts!), html);
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
            Assert.True(Snapshot(Invoke("ReadHtmlDom", html)) == Snapshot(Invoke("ReadHtml", html)), html);
        }
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
        Assert.Equal(Snapshot(Invoke("ReadHtmlDom", html)), Snapshot(parts!));
    }
}
