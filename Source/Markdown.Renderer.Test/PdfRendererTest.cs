using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class PdfRendererTest
{
    [Fact]
    public void FailedSectionsCannotBeSkippedOrPublished()
    {
        var renderer = new PdfRenderer(new HtmlRenderer());
        var session = renderer.CreateSession("Manual",
        [
            new("Home", "index.html", "# Home"),
            new("Broken", "Broken.html", "# Broken\n\n<!-- pdf:hidden -->\nUnclosed region")
        ]);
        Assert.True(session.RenderNextSection());
        using var output = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => session.WritePdf(output));
        Assert.Throws<PdfHiddenContentException>(() => session.RenderNextSection());
        Assert.Throws<InvalidOperationException>(() => session.RenderNextSection());
        Assert.Throws<InvalidOperationException>(() => session.WritePdf(output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void SteppedRenderingPreservesTheCompletePdfBytes()
    {
        var renderer = new PdfRenderer(new HtmlRenderer());
        PdfSourceSection[] sections =
        [
            new("Home", "index.html", "# Home\n\n[Details](Details.md)\n\n:material-home:"),
            new("Details", "Details.html", "# Details\n\n```csharp\nvar value = 42;\n```\n\n<!-- pdf:hidden -->\nSecret\n<!-- /pdf:hidden -->")
        ];
        using var normal = new MemoryStream();
        renderer.WritePdf(normal, "Manual", sections);
        var session = renderer.CreateSession("Manual", sections);
        Assert.True(session.RenderNextSection());
        Assert.True(session.RenderNextSection());
        Assert.False(session.RenderNextSection());
        using var stepped = new MemoryStream();
        session.WritePdf(stepped);
        Assert.Equal(normal.ToArray(), stepped.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsEightWrappedCodeLinesTogetherUsingActualCharacterBoundaries(bool highlighted)
    {
        var text = "CodeStart" + new string('a', 647) + "CodeEnd";
        var code = highlighted
            ? $"<pre><code><span class=\"keyword\">{text[..100]}</span><span class=\"string\">{text[100..]}</span></code></pre>"
            : $"```text\n{text}\n```";
        var result = Layout(Filler(46) + "\n\n" + code);
        var page = PageContaining(result, "CodeStart");
        Assert.Equal(1, page);
        var codeText = string.Concat(ReadTextOperations(result.Pages[page].Content.ToString()).Select(operation => operation.Text));
        Assert.Equal(text, codeText);
        Assert.Equal(8, Regex.Matches(result.Pages[page].Content.ToString(), " re f").Count);
        AssertContentWithinMargins(result);
    }

    [Theory]
    [InlineData("<p></p>")]
    [InlineData("<p><button>Ignored</button></p>")]
    [InlineData("<p><span id=\"empty-anchor\"></span></p>")]
    public void EmptyParagraphsOnlyAddBlockSpacing(string emptyParagraph)
    {
        var baseline = Layout("<p>Before</p><p>After</p>");
        var actual = Layout($"<p>Before</p>{emptyParagraph}<p>After</p>");
        var baselineY = ReadTextOperations(baseline.Pages[0].Content.ToString()).Single(operation => operation.Text == "After").Y;
        var actualY = ReadTextOperations(actual.Pages[0].Content.ToString()).Single(operation => operation.Text == "After").Y;
        Assert.Equal(12, baselineY - actualY, 3);
        if (emptyParagraph.Contains("empty-anchor", StringComparison.Ordinal))
            Assert.True(actual.Destinations.ContainsKey("page.html#empty-anchor"));
    }

    [Theory]
    [InlineData(43)]
    [InlineData(45)]
    [InlineData(47)]
    [InlineData(49)]
    public void KeepsWrappedHeadingChainsWithTheirFirstParagraph(int fillerLines)
    {
        var heading = "HeadingStart " + string.Join(" ", Enumerable.Repeat("long heading", 14)) + " HeadingEnd";
        var result = Layout($"{Filler(fillerLines)}\n\n<h2>{heading}</h2><h3>Subheading</h3>" +
            "<p>BodyStart<br />BodyMiddle<br />BodyEnd</p>");

        var page = PageContaining(result, "HeadingStart");
        Assert.Equal(page, PageContaining(result, "HeadingEnd"));
        Assert.Equal(page, PageContaining(result, "Subheading"));
        Assert.Equal(page, PageContaining(result, "BodyStart"));
        Assert.Equal(page, PageContaining(result, "BodyEnd"));
        AssertContentWithinMargins(result);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(47)]
    [InlineData(49)]
    public void KeepsAlertTitleAndTextTogetherWithoutAnAccentOnThePreviousPage(int fillerLines)
    {
        var result = Layout(Filler(fillerLines) + "\n\n> [!TIP]\n> AlertBody");

        var page = PageContaining(result, "Tip");
        Assert.Equal(page, PageContaining(result, "AlertBody"));
        if (page > 0) Assert.DoesNotContain("2.5 w", result.Pages[page - 1].Content.ToString());
        AssertContentWithinMargins(result);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(47)]
    [InlineData(49)]
    public void KeepsResultTitleAndTextTogether(int fillerLines)
    {
        var result = Layout(Filler(fillerLines) + "\n\n> [!RESULT]\n> ResultBody");

        Assert.Equal(PageContaining(result, "Result"), PageContaining(result, "ResultBody"));
        AssertContentWithinMargins(result);
    }

    [Theory]
    [InlineData(45, 4)]
    [InlineData(47, 6)]
    [InlineData(49, 3)]
    [InlineData(0, 110)]
    public void SplitsParagraphsWithoutLeavingSingleLinesOnEitherPage(int fillerLines, int paragraphLines)
    {
        var paragraph = "<p>" + string.Join("<br />", Enumerable.Range(0, paragraphLines)
            .Select(index => $"ParagraphLine{index:D3}")) + "</p>";
        var result = Layout(Filler(fillerLines) + "\n\n" + paragraph);
        var pages = Enumerable.Range(0, paragraphLines).GroupBy(index => PageContaining(result, $"ParagraphLine{index:D3}"));

        Assert.All(pages, page => Assert.True(page.Count() >= 2, $"One paragraph line on page {page.Key + 1}."));
        AssertContentWithinMargins(result);
    }

    [Fact]
    public void SplitsAnOversizedTableRowAndRepeatsHeadersWithoutLosingLinksOrAnchors()
    {
        var cell = string.Join("<br />", Enumerable.Range(0, 210).Select(index =>
            $"<span id=\"row-{index}\"></span><a href=\"https://example.com/{index}\">CellLine{index:D3}</a>"));
        var result = Layout("<table><tr><th id=\"table-heading\">ColumnHeader</th><th>ValueHeader</th></tr>" +
            $"<tr><td>{cell}</td><td>CompanionCell</td></tr></table>");

        Assert.True(result.Pages.Count >= 3);
        foreach (var page in result.Pages)
        {
            Assert.Contains(Hex("ColumnHeader"), page.Content.ToString());
            Assert.Contains(Hex("CellLine"), page.Content.ToString());
        }
        Assert.Equal(0, result.Destinations["page.html#table-heading"].PageIndex);
        for (var index = 0; index < 210; index++)
        {
            var label = $"CellLine{index:D3}";
            var page = PageContaining(result, label);
            Assert.Equal(page, result.Destinations[$"page.html#row-{index}"].PageIndex);
            Assert.Single(result.Pages.SelectMany(item => item.Links).Where(link => link.Uri == $"https://example.com/{index}"));
            Assert.Equal(1, result.Pages.Sum(item => Regex.Matches(item.Content.ToString(), Hex(label)).Count));
        }
        AssertContentWithinMargins(result);
    }

    [Fact]
    public void KeepsNormalTableRowsTogetherAndStartsWithHeaderAndData()
    {
        var rows = string.Concat(Enumerable.Range(0, 80).Select(index =>
            $"<tr><td>FirstCell{index:D3}<br />SecondLine{index:D3}</td><td>OtherCell{index:D3}</td></tr>"));
        var result = Layout(Filler(49) + "\n\n<table><tr><th>TableHeading</th><th>OtherHeading</th></tr>" + rows + "</table>");

        for (var index = 0; index < 80; index++)
        {
            var page = PageContaining(result, $"FirstCell{index:D3}");
            Assert.Equal(page, PageContaining(result, $"SecondLine{index:D3}"));
            Assert.Equal(page, PageContaining(result, $"OtherCell{index:D3}"));
        }
        foreach (var page in result.Pages.Where(page => page.Content.ToString().Contains(Hex("TableHeading"), StringComparison.Ordinal)))
            Assert.Contains(Hex("FirstCell"), page.Content.ToString());
        AssertContentWithinMargins(result);
    }

    [Fact]
    public void SplitsEvenATableHeaderTallerThanThePageWithoutBlankPages()
    {
        var header = string.Join("<br />", Enumerable.Range(0, 150).Select(index => $"HeaderLine{index:D3}"));
        var result = Layout($"<table><tr><th>{header}</th></tr><tr><td>FinalBodyCell</td></tr></table>");

        Assert.True(result.Pages.Count >= 3);
        Assert.All(result.Pages, page => Assert.Contains(" Tj ET", page.Content.ToString()));
        for (var index = 0; index < 150; index++) PageContaining(result, $"HeaderLine{index:D3}");
        PageContaining(result, "FinalBodyCell");
        AssertContentWithinMargins(result);
    }

    [Fact]
    public void KeepsShortCodeTogetherButLetsLongCodeUseTheRemainingPage()
    {
        var shortCode = string.Join("\n", Enumerable.Range(0, 6).Select(index => $"ShortCode{index:D3}"));
        var shortResult = Layout(Filler(47) + $"\n\n```text\n{shortCode}\n```");
        Assert.Equal(PageContaining(shortResult, "ShortCode000"), PageContaining(shortResult, "ShortCode005"));

        var longCode = string.Join("\n", Enumerable.Range(0, 100).Select(index => $"LongCode{index:D3}"));
        var longResult = Layout(Filler(43) + $"\n\n```text\n{longCode}\n```");
        Assert.Equal(0, PageContaining(longResult, "LongCode000"));
        Assert.True(PageContaining(longResult, "LongCode099") > 0);
        AssertContentWithinMargins(shortResult);
        AssertContentWithinMargins(longResult);
    }

    [Theory]
    [InlineData(43)]
    [InlineData(47)]
    [InlineData(49)]
    public void KeepsHeadingWithTheFollowingDiagram(int fillerLines)
    {
        var result = Layout(Filler(fillerLines) + """


            ## DiagramHeading

            <svg xmlns="http://www.w3.org/2000/svg" width="400" height="400" viewBox="0 0 400 400">
            <rect width="400" height="400" fill="#eeeeee" />
            <text x="20" y="40">DiagramBody</text>
            </svg>
            """);

        Assert.Equal(PageContaining(result, "DiagramHeading"), PageContaining(result, "DiagramBody"));
        AssertContentWithinMargins(result);
    }

    [Fact]
    public void StartsANewSectionAtTheTopWithoutASeparatorOrExtraHeadingSpacing()
    {
        var layout = new PdfLayout(new HtmlRenderer(), "");
        layout.RenderSection(new PdfSourceSection("First", "first.html", Filler(51)));
        layout.RenderSection(new PdfSourceSection("Second", "second.html", "# SecondTitle\n\nBody"));
        var result = layout.Complete();
        var destination = result.Destinations["second.html"];

        Assert.Equal(1, destination.PageIndex);
        Assert.Equal(PdfLayout.PageHeight - PdfLayout.Top, destination.Top, 3);
        var page = result.Pages[destination.PageIndex].Content.ToString();
        Assert.DoesNotContain("0.6 w", page);
        var title = ReadTextOperations(page).Single(operation => operation.Text == "SecondTitle");
        Assert.Equal(PdfLayout.PageHeight - PdfLayout.Top - 22 * 0.95, title.Y, 3);
    }

    [Fact]
    public void HidesAMarkedHeadingAndItsContentUntilTheNextHeadingOfTheSameOrAHigherLevel()
    {
        const string markdown = """
            # FirstTitle

            IntroText

            ## HiddenHeading {.pdf-hidden}

            HiddenText

            ### NestedHeading

            NestedText

            ## PublicHeading

            PublicText

            ### DeepHidden {.pdf-hidden}

            DeepText

            # SecondTitle

            ClosingText
            """;

        var content = string.Concat(Layout(markdown).Pages.Select(page => page.Content.ToString()));

        foreach (var text in new[] { "FirstTitle", "IntroText", "PublicHeading", "PublicText", "SecondTitle", "ClosingText" })
            Assert.Contains(Hex(text), content);
        foreach (var text in new[] { "HiddenHeading", "HiddenText", "NestedHeading", "NestedText", "DeepHidden", "DeepText" })
            Assert.DoesNotContain(Hex(text), content);
    }

    [Fact]
    public void HidesMarkedRegionsAtEveryNestingLevel()
    {
        const string markdown = """
            BeforeText
            <!-- pdf:hidden -->
            HiddenParagraph

            ## HiddenRegionHeading

            - HiddenListItem
            <!-- /pdf:hidden -->

            - KeptItem

              <!-- pdf:hidden -->
              HiddenItemText
              <!-- /pdf:hidden -->

            > QuoteText
            >
            > <!-- pdf:hidden -->
            > HiddenQuoteText
            > <!-- /pdf:hidden -->

            AfterText
            """;

        var content = string.Concat(Layout(markdown).Pages.Select(page => page.Content.ToString()));

        foreach (var text in new[] { "BeforeText", "KeptItem", "QuoteText", "AfterText" })
            Assert.Contains(Hex(text), content);
        foreach (var text in new[] { "HiddenParagraph", "HiddenRegionHeading", "HiddenListItem", "HiddenItemText", "HiddenQuoteText" })
            Assert.DoesNotContain(Hex(text), content);
    }

    [Fact]
    public void IgnoresMarkersInCode()
    {
        const string markdown = """
            ```markdown
            <!-- pdf:hidden -->
            ```

            Inline `<!-- /pdf:hidden -->` StillVisible

            AfterCode
            """;

        var content = string.Concat(Layout(markdown).Pages.Select(page => page.Content.ToString()));

        Assert.Contains(Hex("pdf:hidden"), content);
        Assert.Contains(Hex("StillVisible"), content);
        Assert.Contains(Hex("AfterCode"), content);
    }

    [Theory]
    [InlineData("InlineText <!-- pdf:hidden --> Secret <!-- /pdf:hidden -->", "not inside a paragraph")]
    [InlineData("<!-- pdf:hidden --> SecretTrailingText\n\n<!-- /pdf:hidden -->", "must stand alone")]
    [InlineData("<div>\n<!-- pdf:hidden -->\nSecretInsideDiv\n<!-- /pdf:hidden -->\n</div>", "must stand alone")]
    [InlineData("<div>\n<!-- pdf-hidden -->\n</div>", "is not a valid marker")]
    [InlineData("<div>Intro</div>\n<!-- pdf:hidden -->\n\nSecret\n\n<!-- /pdf:hidden -->", "must stand alone")]
    [InlineData("<!-- pdf:hiden -->\nSecret\n\n<!-- /pdf:hiden -->", "is not a valid marker")]
    [InlineData("<!-- pdf:hide -->", "is not a valid marker")]
    public void RejectsMarkersThatWouldOtherwiseBeIgnored(string markdown, string reason)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains(reason, exception.Message);
    }

    [Fact]
    public void RejectsUnbalancedMarkersInNestedContainersOfAHiddenRange()
    {
        const string markdown = "<!-- pdf:hidden -->\n\n- Item\n\n  <!-- pdf:hidden -->\n\n<!-- /pdf:hidden -->\n";

        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("has no matching", exception.Message);
    }

    [Theory]
    [InlineData("<!-- pdf:hidden -->\nOpen", "has no matching")]
    [InlineData("Text\n\n<!-- /pdf:hidden -->", "has no preceding")]
    [InlineData("<!-- pdf:hidden -->\n\n<!-- pdf:hidden -->\n\n<!-- /pdf:hidden -->", "before the previous one is closed")]
    [InlineData("- Item\n\n  <!-- pdf:hidden -->\n\n<!-- /pdf:hidden -->", "has no matching")]
    [InlineData("## Hidden {.pdf-hidden}\n\n<!-- pdf:hidden -->", "has no matching")]
    public void RejectsUnbalancedMarkersAndNamesThePage(string markdown, string reason)
    {
        var layout = new PdfLayout(new HtmlRenderer(), "");

        var exception = Assert.Throws<PdfHiddenContentException>(() =>
            layout.RenderSection(new PdfSourceSection("Broken page", "guide/broken.html", markdown)));

        Assert.Contains("Broken page", exception.Message);
        Assert.Contains("guide/broken.html", exception.Message);
        Assert.Contains(reason, exception.Message);
    }

    [Fact]
    public void KeepsHiddenContentInHtmlOutput()
    {
        const string markdown = "## Internal {.pdf-hidden}\n\nSecret\n\n<!-- pdf:hidden -->\nRegion\n<!-- /pdf:hidden -->\n";

        var html = new HtmlRenderer().RenderMarkdownFragment(markdown);

        Assert.Contains("Internal", html);
        Assert.Contains("Secret", html);
        Assert.Contains("Region", html);
    }

    [Fact]
    public void DropsAndRenumbersFootnotesReferencedOnlyByHiddenContent()
    {
        const string markdown = """
            <!-- pdf:hidden -->
            Hidden[^hidden].
            <!-- /pdf:hidden -->

            Kept[^kept].

            [^hidden]: HiddenFootnote
            [^kept]: KeptFootnote
            """;

        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.DoesNotContain("HiddenFootnote", html);
        Assert.Contains("KeptFootnote", html);
        Assert.Contains("id=\"fn:1\"", html);
        Assert.DoesNotContain("fn:2", html);
    }

    [Fact]
    public void LeavesHiddenTextOutOfTheCoverDescription()
    {
        const string markdown = """
            # Guide

            <!-- pdf:hidden -->
            SecretIntro
            <!-- /pdf:hidden -->

            PublicIntro
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide", [new PdfSourceSection("Guide", "index.html", markdown)]);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.DoesNotContain(Hex("SecretIntro"), pdf);
        Assert.Contains(Hex("PublicIntro"), pdf);
    }

    [Fact]
    public void HidesAnOpenApiReferenceUnderAHiddenHeading()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-pdf-hidden-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "api.json"), """
                {
                  "openapi": "3.1.0",
                  "info": { "title": "SecretApiTitle", "version": "1" },
                  "paths": { "/launches": { "get": { "summary": "ListLaunches", "responses": { "200": { "description": "SecretResponse" } } } } }
                }
                """);
            var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                "# Guide\n\n## Internal API {.pdf-hidden}\n\n```openapi\napi.json\n```\n\nAfterApiText\n\n## PublicHeading\n\nPublicText\n",
                Path.Combine(directory, "guide.md"),
                includeSpecificationPageLink: false);

            var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

            foreach (var text in new[] { "Internal API", "API overview", "SecretApiTitle", "ListLaunches", "SecretResponse", "AfterApiText" })
                Assert.DoesNotContain(text, html);
            Assert.Contains("PublicHeading", html);
            Assert.Contains("PublicText", html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("<!--pdf:hidden-->", "<!--/pdf:hidden-->")]
    [InlineData("<!-- PDF:Hidden -->", "<!-- / PDF:HIDDEN -->")]
    [InlineData("<!--   pdf:hidden   -->", "<!--   /pdf:hidden   -->")]
    [InlineData("<!-- pdf: hidden -->", "<!-- / pdf : hidden -->")]
    public void RecognizesMarkerSpellingVariants(string start, string end)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment($"Before\n\n{start}\nSecretText\n{end}\n\nAfter");

        Assert.DoesNotContain("SecretText", html);
        Assert.Contains("Before", html);
        Assert.Contains("After", html);
    }

    [Fact]
    public void HandlesSetextHeadingsAndWindowsLineEndings()
    {
        const string markdown = "Intro\r\n\r\nHidden setext {.pdf-hidden}\r\n-------------\r\n\r\nSetextSecret\r\n\r\n<!-- pdf:hidden -->\r\nRegionSecret\r\n<!-- /pdf:hidden -->\r\n\r\nVisible heading\r\n---------------\r\n\r\nVisibleText\r\n";

        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.DoesNotContain("Hidden setext", html);
        Assert.DoesNotContain("SetextSecret", html);
        Assert.DoesNotContain("RegionSecret", html);
        Assert.Contains("Visible heading", html);
        Assert.Contains("VisibleText", html);
    }

    [Fact]
    public void KeepsFootnotesOfVisibleTextWhenTheLastSectionIsHidden()
    {
        const string markdown = """
            Kept[^kept].

            [^kept]: KeptFootnote

            ## Internal {.pdf-hidden}

            Hidden[^hidden].

            [^hidden]: HiddenFootnote
            """;

        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.Contains("KeptFootnote", html);
        Assert.DoesNotContain("HiddenFootnote", html);
        Assert.Contains("id=\"fn:1\"", html);
    }

    [Fact]
    public void DropsFootnotesReachableOnlyThroughHiddenFootnotesAndDeadBackLinks()
    {
        const string markdown = """
            <!-- pdf:hidden -->
            Hidden[^outer] and[^shared].
            <!-- /pdf:hidden -->

            Kept[^shared].

            [^outer]: OuterFootnote[^inner]
            [^inner]: InnerFootnote
            [^shared]: SharedFootnote
            """;

        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.DoesNotContain("OuterFootnote", html);
        Assert.DoesNotContain("InnerFootnote", html);
        Assert.Contains("SharedFootnote", html);
        var targets = Regex.Matches(html, @"href=""#(?<id>fnref:\d+)""").Select(match => match.Groups["id"].Value).ToArray();
        Assert.NotEmpty(targets);
        Assert.All(targets, id => Assert.Contains($"id=\"{id}\"", html));
    }

    [Fact]
    public void EndsAHiddenSectionAtTheEndOfAnEnclosingHtmlElement()
    {
        const string markdown = """
            <details>
            <summary>Summary</summary>

            ## Hidden {.pdf-hidden}

            DetailsSecret

            </details>

            AfterDetails

            ## Also hidden {.pdf-hidden}

            <details>
            <summary>InnerSummary</summary>

            InnerSecret

            </details>

            TrailingSecret

            ## PublicHeading
            """;
        var layout = new PdfLayout(new HtmlRenderer(), "");

        layout.RenderSection(new PdfSourceSection("Details", "details.html", markdown));

        var content = string.Concat(layout.Complete().Pages.Select(page => page.Content.ToString()));
        Assert.Contains(Hex("AfterDetails"), content);
        Assert.Contains(Hex("PublicHeading"), content);
        foreach (var text in new[] { "DetailsSecret", "InnerSummary", "InnerSecret", "TrailingSecret" })
            Assert.DoesNotContain(Hex(text), content);
    }

    [Theory]
    [InlineData("<!-- pdf-hidden -->")]
    [InlineData("<!--PDF_hidden-->")]
    [InlineData("<!-- /pdf hidden -->")]
    public void RejectsMarkerLookalikes(string marker)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() =>
            new HtmlRenderer().RenderPdfMarkdownFragment($"Before\n\n{marker}\n\nAfter"));

        Assert.Contains(marker, exception.Message);
        Assert.Contains("<!-- pdf:hidden -->", exception.Message);
    }

    [Fact]
    public void KeepsOrdinaryCommentsThatMentionTheMarkerWords()
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment("Before\n\n<!-- remove the pdf hidden flag later -->\n\nAfter");

        Assert.Contains("Before", html);
        Assert.Contains("After", html);
    }

    [Fact]
    public void RejectsAHiddenSectionThatEndsInsideAnHtmlBlock()
    {
        const string markdown = """
            <details>
            <summary>Summary</summary>

            ## Hidden {.pdf-hidden}

            <p>SecretParagraph</p>
            </details>

            After
            """;

        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("\"Hidden\"", exception.Message);
        Assert.Contains("</details> closes an element that starts before it", exception.Message);
    }

    [Theory]
    [InlineData("<script>for (let i = 0; i<items.length; i++) { items[i].onclick = () => go(i); }</script>")]
    [InlineData("<script>el.innerHTML = \"<p>Hello\";</script>")]
    [InlineData("<script>\n//<![CDATA[\nconst template = \"<div>\";\n//]]>\n</script>")]
    [InlineData("<style>.a > .b { color: red; }</style>")]
    [InlineData("<div title=\"a > b\" data-x='c>d'><DIV>Secret</Div></div>")]
    public void AcceptsValidHtmlInsideHiddenSections(string block)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment($"## Hidden {{.pdf-hidden}}\n\n{block}\n\n## Next\n\nAfterText\n");

        Assert.Contains("AfterText", html);
        Assert.DoesNotContain("Hidden", html);
    }

    [Fact]
    public void RejectsAHiddenSectionThatEndsInsideAParagraph()
    {
        const string markdown = """
            <details>
            <summary>Summary</summary>

            ## Internal {.pdf-hidden}

            SecretInsideDetails</details>

            After
            """;

        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("</details> closes an element that starts before it", exception.Message);
    }

    [Fact]
    public void RepairsUnclosedHtml()
    {
        var layout = new PdfLayout(new HtmlRenderer(), "");

        layout.RenderSection(new PdfSourceSection("Broken HTML", "broken/index.html", "<div>\n\nOpen"));

        Assert.Contains(layout.Complete().Pages.SelectMany(page => ReadTextOperations(page.Content.ToString())), operation => operation.Text.Contains("Open"));
    }

    [Theory]
    [InlineData("<pre>if (a < b) m;</pre>", "if (a < b) m;")]
    [InlineData("<textarea>Map<String, Integer> m;</textarea>", "Map<String, Integer> m;")]
    [InlineData("<a href=/docs/>Docs</a>", "Docs")]
    [InlineData("<ul><li>One<li>Two</ul>", "Two")]
    [InlineData("<b><i>Text</b></i>", "Text")]
    [InlineData("<p>&amp;lt; remains text</p>", "&lt; remains text")]
    [InlineData("<script>if (a < b && c > d) { run(); }</script>\n\nVisible", "Visible")]
    public void RendersBrowserParsedHtml(string markdown, string expected)
    {
        var layout = Layout(markdown);
        var text = string.Concat(layout.Pages.SelectMany(page => ReadTextOperations(page.Content.ToString())).Select(operation => operation.Text));
        Assert.Contains(expected, text);
        using var output = new MemoryStream();
        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide", [new PdfSourceSection("Guide", "index.html", markdown)]);
        Assert.True(output.Length > 0);
    }

    [Fact]
    public void UnclosedTemplateHidesFollowingContent()
    {
        var document = PdfHtmlDocument.Parse("<p>Before</p><template/>Hidden<p>Also hidden</p>");
        Assert.Equal("Before", document.DocumentElement!.InnerText);
    }

    [Fact]
    public void PreservesWhitespaceBetweenHighlightedCodeSpans()
    {
        var result = Layout("```json\n{\n  \"name\": \"value\"\n}\n```");
        var text = result.Pages.SelectMany(page => ReadTextOperations(page.Content.ToString())).ToArray();
        var open = Assert.Single(text, operation => operation.Text == "{");
        var name = Assert.Single(text, operation => operation.Text == "\"name\"");
        var close = Assert.Single(text, operation => operation.Text == "}");
        Assert.True(open.Y > name.Y && name.Y > close.Y);
        Assert.True(name.X > open.X);
    }

    [Theory]
    [InlineData("# SecretTitle {.pdf-hidden}\n\nBody", true)]
    [InlineData("<!-- pdf:hidden -->\n# SecretTitle\n<!-- /pdf:hidden -->\n\nBody", true)]
    [InlineData("# Title\n\n## Hidden {.pdf-hidden}\n\nBody", false)]
    [InlineData("Body without a title", false)]
    public void DetectsAnExcludedTitleHeading(string markdown, bool excluded)
    {
        Assert.Equal(excluded, new PdfRenderer(new HtmlRenderer()).ExcludesTitleHeading(markdown));
    }

    [Fact]
    public void EndsAHiddenSectionAtTheClosingTagOfAnEnclosingTableCell()
    {
        const string markdown = """
            <table><tr><td>

            ## Example {.pdf-hidden}

            CellSecret

            </td>

            </tr>

            </table>

            AfterTable
            """;
        var layout = new PdfLayout(new HtmlRenderer(), "");

        layout.RenderSection(new PdfSourceSection("Table", "table.html", markdown));

        var content = string.Concat(layout.Complete().Pages.Select(page => page.Content.ToString()));
        Assert.DoesNotContain(Hex("CellSecret"), content);
        Assert.Contains(Hex("AfterTable"), content);
    }

    [Theory]
    [InlineData("<script>\n//<![CDATA[\nconst marker = \"<!-- pdf:hidden -->\";\n//]]>\n</script>")]
    [InlineData("<script>\n// <!-- pdf:hidden -->\n</script>")]
    [InlineData("<pre>&lt;!-- pdf:hidden --&gt; &lt;!-- /pdf:hidden --&gt;</pre>")]
    [InlineData("Text <script>// <!-- pdf:hidden --></script> more text")]
    [InlineData("Text <style>/* <!-- /pdf:hidden --> */</style> more text")]
    public void IgnoresMarkersInRawTextElements(string block)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment($"Before\n\n{block}\n\nAfter");

        Assert.Contains("Before", html);
        Assert.Contains("After", html);
    }

    [Theory]
    [InlineData("<iframe src=\"https://example.org\"></iframe>")]
    [InlineData("<details>\n<summary>Summary</summary>\nDetailsSecret\n</details>")]
    public void AcceptsAnEndMarkerDirectlyAfterHtml(string html)
    {
        var rendered = new HtmlRenderer().RenderPdfMarkdownFragment($"Before\n\n<!-- pdf:hidden -->\n{html}\n<!-- /pdf:hidden -->\n\nAfter");

        Assert.DoesNotContain("iframe", rendered);
        Assert.DoesNotContain("DetailsSecret", rendered);
        Assert.Contains("Before", rendered);
        Assert.Contains("After", rendered);
    }

    [Fact]
    public void EndsAHiddenSectionAtTheClosingTagOfAnEnclosingSvgElement()
    {
        const string markdown = """
            <svg width="100" height="100" xmlns="http://www.w3.org/2000/svg">
            <foreignObject width="100" height="100">

            ## Example {.pdf-hidden}

            SvgSecret

            </foreignObject>

            </svg>

            Outside
            """;
        var layout = new PdfLayout(new HtmlRenderer(), "");

        layout.RenderSection(new PdfSourceSection("Svg", "svg.html", markdown));

        var content = string.Concat(layout.Complete().Pages.Select(page => page.Content.ToString()));
        Assert.DoesNotContain(Hex("SvgSecret"), content);
        Assert.Contains(Hex("Outside"), content);
    }

    [Fact]
    public void RejectsARangeWrittenInsidePre()
    {
        const string markdown = "<pre>\n\n<!-- pdf:hidden -->\n\nSecretInsidePre\n\n<!-- /pdf:hidden -->\n\n</pre>";

        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("must stand alone", exception.Message);
    }

    [Theory]
    [InlineData("<details>\n<summary>Open</summary>\n\n<!-- pdf:hidden -->\nSecret\n</details>\n<!-- /pdf:hidden -->")]
    [InlineData("<details>\n<summary>Open</summary>\n\n<!-- pdf:hidden -->\n\nSecret\n\n</details>\n\n<!-- /pdf:hidden -->")]
    public void RejectsARangeThatClosesAnElementOpenedBeforeIt(string markdown)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("closes an element that starts before it", exception.Message);
    }

    [Theory]
    [InlineData("Text <pre><!-- pdf:hidden -->SecretInlinePre<!-- /pdf:hidden --></pre> end.", "not inside a paragraph")]
    [InlineData("Text <!-- note <pre> --> and <!-- pdf:hidden --> SecretAfterNote <!-- /pdf:hidden --> end.", "not inside a paragraph")]
    [InlineData("Inline <textarea><!-- pdf:hidden -->SecretInlineTextarea<!-- /pdf:hidden --></textarea> after.", "not inside a paragraph")]
    [InlineData("<textarea>\n<!-- pdf:hidden -->\nSecretInTextarea\n<!-- /pdf:hidden -->\n</textarea>", "must stand alone")]
    [InlineData("<title><!-- pdf:hidden --></title>", "must stand alone")]
    [InlineData("Text <script src=\"a.js\"/> and <!-- pdf:hidden --> SecretAfterScript <!-- /pdf:hidden --> end.", "not inside a paragraph")]
    public void RejectsMarkersInDisplayedRawText(string markdown, string reason)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains(reason, exception.Message);
    }

    [Theory]
    [InlineData("<script>const example = \"<pre><!-- pdf:hidden --></pre>\";</script>")]
    [InlineData("<style>/* <pre><!-- /pdf:hidden --></pre> */</style>")]
    [InlineData("Text <script>const example = \"<pre><!-- pdf:hidden --></pre>\";</script> more")]
    public void IgnoresPreLikeTextInScripts(string markdown)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment($"Before\n\n{markdown}\n\nAfter");

        Assert.Contains("After", html);
    }

    [Fact]
    public void ChecksRangesInDocumentOrderAcrossNestedContainers()
    {
        var balanced = new HtmlRenderer().RenderPdfMarkdownFragment(
            "Intro\n\n<!-- pdf:hidden -->\n\n> Text <span>open\n>\n> </span>\n\n<!-- /pdf:hidden -->\n\nAfter");
        Assert.Contains("After", balanced);
        Assert.DoesNotContain("open", balanced);

        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(
            "<details>\n<summary>Outer</summary>\n\n<!-- pdf:hidden -->\n\n> Close </details>\n>\n> <details>\n\n<!-- /pdf:hidden -->\n\n</details>"));
        Assert.Contains("closes an element that starts before it", exception.Message);
    }

    [Theory]
    [InlineData("## Hidden {.pdf-hidden}\n\nText <script>const s = \"</details>\";</script> more\n\n## Next\n\nNextText")]
    [InlineData("<!-- pdf:hidden -->\n\nText <script>const s = \"</details>\";</script> more\n\n<!-- /pdf:hidden -->\n\nNextText")]
    public void IgnoresClosingTagsInInlineScripts(string markdown)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.Contains("NextText", html);
        Assert.DoesNotContain("const s", html);
    }

    [Fact]
    public void LeavesScriptsStylesAndTemplatesOutOfThePdf()
    {
        const string markdown = """
            # Guide

            Intro <script>var inline = "InlineScriptText";</script> text.

            <script>
            <!-- pdf:hidden -->
            var secret = "ScriptRangeSecret";
            <!-- /pdf:hidden -->
            </script>

            <style>.x { content: "StyleText"; }</style>

            <template><p>TemplateText</p></template>

            <noscript>NoscriptText</noscript>

            After
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide", [new PdfSourceSection("Guide", "index.html", markdown)]);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        foreach (var text in new[] { "InlineScriptText", "ScriptRangeSecret", "StyleText", "TemplateText" })
            Assert.DoesNotContain(Hex(text), pdf);
        Assert.Contains(Hex("NoscriptText"), pdf);
        Assert.Contains(Hex("After"), pdf);
        Assert.Contains(Hex("Intro"), pdf);
    }

    [Theory]
    [InlineData("<pre><script>\n<!-- pdf:hidden -->\nPreScriptSecret\n<!-- /pdf:hidden -->\n</script>PreVisible</pre>")]
    [InlineData("<pre><template><p>PreTemplateSecret</p></template>PreVisible</pre>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"40\"><foreignObject width=\"40\" height=\"40\"><template><svg><text>SvgTemplateSecret</text></svg></template></foreignObject></svg>\n\nPreVisible")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"40\">\n<style>text{fill:red}</style>\n</svg>\n\nPreVisible")]
    public void RemovesScriptsStylesAndTemplatesFromEveryLayoutPath(string markdown)
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide", [new PdfSourceSection("Guide", "index.html", markdown)],
            includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains(Hex("PreVisible"), pdf);
        foreach (var text in new[] { "PreScriptSecret", "PreTemplateSecret", "SvgTemplateSecret", "run()", "fill:red" })
            Assert.DoesNotContain(Hex(text), pdf);
    }

    [Fact]
    public void KeepsAHeadingWithTheParagraphAfterAScript()
    {
        for (var lines = 20; lines <= 60; lines++)
        {
            var result = Layout($"{Filler(lines)}\n\n## HeadingBeforeScript\n\n<script>var x = 1;</script>\n\nParagraphAfterScript");

            Assert.Equal(PageContaining(result, "HeadingBeforeScript"), PageContaining(result, "ParagraphAfterScript"));
        }
    }

    [Theory]
    [InlineData("## Hidden {.pdf-hidden}\n\n<div>\n\nSecret\n\n## Next\n", "<div> is not closed before the section ends")]
    [InlineData("## Hidden {.pdf-hidden}\n\n<p>Secret\n\n## Next\n", "<p> is not closed before the section ends")]
    [InlineData("## Hidden {.pdf-hidden}\n\nValues are List<String> here.\n\n## Next\n", "<String> is not closed before the section ends")]
    [InlineData("## Hidden {.pdf-hidden}\n\nSecret <b><i>both</b> italic</i> text.\n\n## Next\n", "</b> does not match the open <i>")]
    [InlineData("## Hidden {.pdf-hidden}\n\n<details>\n<summary>S</summary>\n\n## Inner\n\nSecret\n\n</details>\n\n## Next\n", "<details> is not closed before the section ends")]
    [InlineData("<!-- pdf:hidden -->\n\n<div>\n\nSecret\n\n<!-- /pdf:hidden -->\n\n</div>", "<div> is not closed")]
    [InlineData("<!-- pdf:hidden -->\n\nValues are List<String> here.\n\n<!-- /pdf:hidden -->", "<String> is not closed")]
    public void RejectsIncompleteHtmlInExcludedContent(string markdown, string problem)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains(problem, exception.Message);
    }

    [Theory]
    [InlineData("<style-guide>\nVisibleGuide\n</style-guide>\n\nMiddleVisible\n\n<template-card>\nVisibleCard\n</template-card>\n\n<style>.x { color: red; }</style>")]
    [InlineData("<!-- Remember to remove the <script> tag later -->\n\nMiddleVisible\n\n<script>var x = 1;</script>")]
    [InlineData("<!-- <script> -->\n\nMiddleVisible\n\n<!-- </script> -->")]
    [InlineData("<script src=\"analytics.js\"></script>\n\nMiddleVisible\n\n<script>var x = 1;</script>")]
    [InlineData("<template></template>\n\nMiddleVisible\n\n<template>var x</template>")]
    [InlineData("<![CDATA[ <script> ]]>\n\nMiddleVisible\n\n<script>var x = 1;</script>")]
    [InlineData("<textarea><script>MiddleVisible</script></textarea>")]
    public void RemovesOnlyRealScriptsStylesAndTemplates(string markdown)
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide", [new PdfSourceSection("Guide", "index.html", markdown)],
            includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains(Hex("MiddleVisible"), pdf);
        Assert.DoesNotContain(Hex("var x"), pdf);
    }

    [Theory]
    [InlineData("Visible[^v].\n\n## Hidden {.pdf-hidden}\n\n[^v]: SecretDefinition\n\nHidden text.\n")]
    [InlineData("Visible[^v].\n\n<!-- pdf:hidden -->\n\n[^v]: SecretDefinition\n\n<!-- /pdf:hidden -->\n\nAfter\n")]
    public void RejectsFootnoteDefinitionsInExcludedContentThatVisibleTextUses(string markdown)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("Footnote [^v] is defined in excluded content", exception.Message);
    }

    [Theory]
    [InlineData("Visible[^v] [Link][r].\n\n## Hidden {.pdf-hidden}\n\nHidden text.\n\n[^v]: KeptDefinition\n\n[r]: https://kept.example\n")]
    [InlineData("Visible[^v] [Link][r].\n\n## Hidden {.pdf-hidden}\n\nHidden text.\n\n[^v]: KeptDefinition\n\n[r]: https://kept.example\n\n## Next\n\nAfter\n")]
    public void KeepsDefinitionsWrittenBelowTheLastBlockOfAHiddenSection(string markdown)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.DoesNotContain("Hidden text", html);
        Assert.Contains("KeptDefinition", html);
        Assert.Contains("href=\"https://kept.example\"", html);
    }

    [Theory]
    [InlineData("Visible.\n\n## Hidden {.pdf-hidden}\n\nHidden[^h] text.\n\n[^h]: SecretDefinition\n")]
    [InlineData("Visible[^v].\n\n[^v]: KeptDefinition\n\n<!-- pdf:hidden -->\n\nHidden[^h].\n\n[^h]: SecretDefinition\n\n<!-- /pdf:hidden -->\n")]
    public void DropsFootnoteDefinitionsInExcludedContentThatOnlyExcludedTextUses(string markdown)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.DoesNotContain("SecretDefinition", html);
        Assert.DoesNotContain("Hidden", html);
    }

    [Theory]
    [InlineData("[Visible][secret]\n\n<!-- pdf:hidden -->\n[secret]: https://secret.example/confidential\n<!-- /pdf:hidden -->\n")]
    [InlineData("![Logo][secret]\n\n## Hidden {.pdf-hidden}\n\n[secret]: https://secret.example/logo.png\n\nHidden text.\n")]
    public void RejectsVisibleLinksToExcludedLinkDefinitions(string markdown)
    {
        var exception = Assert.Throws<PdfHiddenContentException>(() => new HtmlRenderer().RenderPdfMarkdownFragment(markdown));

        Assert.Contains("Link reference [secret] is defined in excluded content", exception.Message);
    }

    [Fact]
    public void AcceptsLinkDefinitionsInExcludedContentThatOnlyExcludedTextUses()
    {
        const string markdown = "[Kept][kept]\n\n[kept]: https://kept.example\n\n<!-- pdf:hidden -->\n[Hidden][secret]\n\n[secret]: https://secret.example\n<!-- /pdf:hidden -->\n";

        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown);

        Assert.Contains("https://kept.example", html);
        Assert.DoesNotContain("secret.example", html);
    }

    private static PdfLayoutResult Layout(string markdown)
    {
        var layout = new PdfLayout(new HtmlRenderer(), "");
        layout.RenderSection(new PdfSourceSection("Pagination", "page.html", markdown));
        return layout.Complete();
    }

    private static string Filler(int lines) => lines == 0 ? "" :
        "<p>" + string.Join("<br />", Enumerable.Range(0, lines).Select(index => $"Filler{index:D3}")) + "</p>";

    private static string Hex(string text) => Convert.ToHexString(Encoding.Latin1.GetBytes(text));

    private static int PageContaining(PdfLayoutResult result, string text) =>
        Assert.Single(result.Pages.Select((page, index) => (page, index))
            .Where(item => item.page.Content.ToString().Contains(Hex(text), StringComparison.Ordinal))).index;

    private static IEnumerable<(double X, double Y, string Text)> ReadTextOperations(string content) =>
        Regex.Matches(content, @"BT /F\d+ [\d.]+ Tf 1 0 0 1 (?<x>[-\d.]+) (?<y>[-\d.]+) Tm <(?<hex>[0-9A-F]*)> Tj ET")
            .Select(match => (
                double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                Encoding.Latin1.GetString(Convert.FromHexString(match.Groups["hex"].Value))));

    private static void AssertContentWithinMargins(PdfLayoutResult result)
    {
        foreach (var page in result.Pages)
        {
            foreach (var operation in ReadTextOperations(page.Content.ToString()))
            {
                Assert.InRange(operation.Y, PdfLayout.Bottom, PdfLayout.PageHeight - PdfLayout.Top);
                Assert.InRange(operation.X, PdfLayout.Left, PdfLayout.PageWidth - PdfLayout.Right);
            }
            foreach (var link in page.Links)
            {
                Assert.InRange(link.Bottom, PdfLayout.Bottom, PdfLayout.PageHeight - PdfLayout.Top);
                Assert.InRange(link.Top, PdfLayout.Bottom, PdfLayout.PageHeight - PdfLayout.Top);
            }
        }
    }

    [Fact]
    public void KeepsHeroContentButOmitsShaderAndPlaygroundControlsInPdf()
    {
        using var output = new MemoryStream();
        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Hero",
            [new PdfSourceSection("Hero", "index.html", """
                # Hero

                ```hero
                title: BannerTitleQ7
                text: BannerTextQ7
                effect: shader
                playground: true
                shader: |
                  // ShaderSentinelQ7 < & >
                  void mainImage(out vec4 c, in vec2 p) { c = vec4(1.0); }
                actions:
                  - text: BannerActionQ7
                    link: https://example.com
                ```
                """)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        foreach (var text in new[] { "BannerTitleQ7", "BannerTextQ7", "BannerActionQ7" })
            Assert.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes(text)), pdf);
        foreach (var text in new[] { "ShaderSentinelQ7", "fragment shader", "Try a hero effect", "Apply shader", "Copy YAML" })
            Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(text)), pdf);
    }

    [Fact]
    public void KeepsMarkedPlaygroundInHtmlButOmitsItFromPdf()
    {
        const string markdown = """
            # Hero Banner

            <!-- pdf:hidden -->
            ```hero
            title: PlaygroundTitleQ8
            text: PlaygroundTextQ8
            effect: connections
            width: content
            playground: true
            actions:
              - text: PlaygroundActionQ8
                link: "#overview"
            ```

            ## Try it

            PlaygroundInstructionsQ8
            <!-- /pdf:hidden -->

            ## Overview

            ReferenceOverviewQ8

            ## Syntax

            ReferenceSyntaxQ8
            """;
        var renderer = new HtmlRenderer();
        var html = renderer.RenderMarkdownFragment(markdown);

        Assert.Contains("class=\"hero-playground-template\"", html);
        foreach (var text in new[] { "PlaygroundTitleQ8", "PlaygroundTextQ8", "PlaygroundActionQ8", "PlaygroundInstructionsQ8" })
            Assert.Contains(text, html);

        using var output = new MemoryStream();
        new PdfRenderer(renderer).WritePdf(output, "Hero Banner",
            [new PdfSourceSection("Hero Banner", "index.html", markdown)]);
        var pdf = Encoding.Latin1.GetString(output.ToArray());

        foreach (var text in new[] { "PlaygroundTitleQ8", "PlaygroundTextQ8", "PlaygroundActionQ8", "PlaygroundInstructionsQ8", "Try it", "Copy YAML" })
            Assert.DoesNotContain(Hex(text), pdf);
        foreach (var text in new[] { "ReferenceOverviewQ8", "ReferenceSyntaxQ8" })
            Assert.Contains(Hex(text), pdf);
    }

    [Fact]
    public void CompositeStatePdfLinkCoversItsWholeFrameAndKeepsTheChildDestination()
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(StaticCode.Diagrams.MermaidRenderer.RenderSvg("""
            stateDiagram-v2
            state Composite {
                Inner
                click Inner href inner.md
            }
            click Composite href outer.md
            """));
        var svg = document.DocumentElement!;
        var frame = svg.GetElementsByTagName("rect").OfType<System.Xml.XmlElement>()
            .Single(element => element.GetAttribute("class") == "diagram-container-shape");
        var dimensions = PdfSvgRenderer.Measure(svg);
        var links = new List<(string Href, double Left, double Bottom, double Right, double Top)>();

        PdfSvgRenderer.Draw(svg, new StringBuilder(), 0, 0, dimensions.Width, dimensions.Height, 800,
            (href, left, bottom, right, top) => links.Add((href, left, bottom, right, top)));

        Assert.Equal(new[] { "outer.html", "inner.html" }, links.Select(link => link.Href));
        var outer = links[0];
        var inner = links[1];
        Assert.Equal(double.Parse(frame.GetAttribute("width"), CultureInfo.InvariantCulture), outer.Right - outer.Left, 2);
        Assert.Equal(double.Parse(frame.GetAttribute("height"), CultureInfo.InvariantCulture), outer.Top - outer.Bottom, 2);
        Assert.True(inner.Left > outer.Left && inner.Right < outer.Right);
        Assert.True(inner.Bottom > outer.Bottom && inner.Top < outer.Top);
    }

    [Theory]
    [InlineData("material-design-icons")]
    [InlineData("alacritty")]
    public void RendersThemeAwareMonochromeIconsInPdfTextColor(string name)
    {
        Assert.True(StaticCode.Icons.IconCatalog.TryGetIcon("dashboard-" + name, out var icon));
        Assert.Contains("currentColor", icon.Glyph.Body);
        var document = new System.Xml.XmlDocument();
        document.LoadXml(icon.Glyph.ToSvg());
        var actual = new StringBuilder();
        PdfSvgRenderer.Draw(document.DocumentElement!, actual, 0, 0, 24, 24, 800);

        document.LoadXml(icon.Glyph.ToSvg().Replace("currentColor", "#1d1d1f", StringComparison.Ordinal));
        var expected = new StringBuilder();
        PdfSvgRenderer.Draw(document.DocumentElement!, expected, 0, 0, 24, 24, 800);

        Assert.Equal(expected.ToString(), actual.ToString());
        Assert.Contains(" rg", actual.ToString());
    }

    [Fact]
    public void AppliesExplicitDashboardIconColorsInPdf()
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Icon",
            [new PdfSourceSection("Icon", "icon/index.html", "- :dashboard-files:{ style=\"color: #16803c\" } [Files](https://files.example.org)")],
            includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("0.086 0.502 0.235 rg", pdf);
    }

    [Fact]
    public void SkipsIsometricParticlesInPdf()
    {
        const string withoutParticle = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <g class="iso-edge"><path class="iso-edge-line" d="M0 0 L24 24"/></g>
            </svg>
            """;
        const string withParticle = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <g class="iso-edge"><path class="iso-edge-line" d="M0 0 L24 24"/><g class="iso-flow"><circle class="iso-particle" r="3"><animateMotion dur="2s" path="M0 0 L24 24"/></circle></g></g>
            </svg>
            """;
        var expected = new StringBuilder();
        var actual = new StringBuilder();
        var document = new System.Xml.XmlDocument();

        document.LoadXml(withoutParticle);
        PdfSvgRenderer.Draw(document.DocumentElement!, expected, 0, 0, 24, 24, 800);
        document.LoadXml(withParticle);
        PdfSvgRenderer.Draw(document.DocumentElement!, actual, 0, 0, 24, 24, 800);

        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Fact]
    public void SkipsDarkIsometricIconVariantInPdf()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <g class="iso-icon-light"><rect x="0" y="0" width="12" height="24" fill="#00ff00"/></g>
              <g class="iso-icon-dark"><rect x="12" y="0" width="12" height="24" fill="#ff0000"/></g>
            </svg>
            """;
        var document = new System.Xml.XmlDocument();
        document.LoadXml(svg);
        var output = new StringBuilder();

        PdfSvgRenderer.Draw(document.DocumentElement!, output, 0, 0, 24, 24, 800);

        Assert.Contains("0 1 0 rg", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("1 0 0 rg", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DrawsIsometricDiagramInLastPhaseInPdf()
    {
        const string markdown = """
            ```isometric
            zone backend "Backend" at 0,0
              api "API" icon material-server
              db  "DB"  icon material-database
            end
            client "Client" icon material-account at 0,1
            phase Start
              client --> api : request
            phase Done
              api --> db : query
            ```
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Isometric",
        [new PdfSourceSection("Isometric", "isometric/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains($"<{Convert.ToHexString(Encoding.ASCII.GetBytes("API"))}>", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain($"<{Convert.ToHexString(Encoding.ASCII.GetBytes("Start"))}> Tj", pdf, StringComparison.Ordinal);
        // Label backgrounds are white, and connection lines are stroked with a width instead of filled.
        Assert.Contains("1 1 1 rg", pdf, StringComparison.Ordinal);
        Assert.Contains("1.75 w", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersVideoSourcesAsPdfLinksWithoutFallbackText()
    {
        const string markdown = """
            <video controls playsinline preload="none" width="960" height="540">
              <source src="https://example.com/demo.mp4" type="video/mp4">
              Your browser does not support embedded videos.
            </video>

            <video src="https://example.com/direct.mp4" controls>
              Direct video fallback.
            </video>
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Videos",
        [new PdfSourceSection("Videos", "videos/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("/URI (https://example.com/demo.mp4)", pdf);
        Assert.Contains("/URI (https://example.com/direct.mp4)", pdf);
        Assert.DoesNotContain($"<{Convert.ToHexString(Encoding.ASCII.GetBytes("Your browser does not support embedded videos."))}> Tj", pdf);
        Assert.DoesNotContain($"<{Convert.ToHexString(Encoding.ASCII.GetBytes("Direct video fallback."))}> Tj", pdf);
    }

    [Fact]
    public void DoesNotNormalizeBooleanWordsInPdfTextOrCodeBlocks()
    {
        const string markdown = """
            Use the player controls to start the video. Wrap it in a loop for testing.
            The sound stays muted until you press play.

            ```html
            <video controls playsinline muted>
              <!-- then loop forever -->
            </video>
            ```
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Videos",
        [new PdfSourceSection("Videos", "videos/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        foreach (var text in new[] { "controls", "playsinline", "muted", "loop" })
            Assert.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes(text)), pdf);
        foreach (var text in new[] { "controls=\"controls\"", "loop=\"loop\"", "muted=\"muted\"", "playsinline=\"playsinline\"" })
            Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(text)), pdf);
    }

    [Fact]
    public void DoesNotNormalizeBooleanWordsInsideAttributeValues()
    {
        const string markdown = """
            ![Player controls overview](https://example.com/shot.png)

            [Docs](https://example.com/docs "Loop settings")
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Media",
        [new PdfSourceSection("Media", "media/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.StartsWith("%PDF-1.7", pdf);
        Assert.Contains("/URI (https://example.com/docs)", pdf);
    }

    [Fact]
    public void RendersTablesInsideScrollableContainersToPdf()
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Tables",
        [
            new PdfSourceSection("Tables", "tables/index.html", "| Header | Value |\n| --- | --- |\n| Wrapped table | preserved |\n")
        ], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("<57726170706564207461626C65> Tj", pdf);
        Assert.Contains("<707265736572766564> Tj", pdf);
    }

    [Fact]
    public void PreservesInlineFormattingLinksAndKeysInsideTables()
    {
        const string markdown = """
            | Shortcut | Details |
            | --- | --- |
            | ++ctrl+cmd+space++ | **Bold**, `code`, and [manual](https://example.com/manual) |
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Table",
        [new PdfSourceSection("Table", "table/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("<4374726C> Tj", pdf);
        Assert.Contains("<436D64> Tj", pdf);
        Assert.Contains("<5370616365> Tj", pdf);
        Assert.Equal(1, pdf.Split("<4374726C> Tj").Length - 1);
        Assert.Contains("/F2 8.5 Tf", pdf);
        Assert.Contains("/F5 8.5 Tf", pdf);
        Assert.Contains("/URI (https://example.com/manual)", pdf);
    }

    [Fact]
    public void AddsInternalPdfLinksForFootnotes()
    {
        const string markdown = """
            A note[^source].

            [^source]: FootnoteContentQ7
            """;
        var filler = string.Join("\n\n", Enumerable.Repeat("Filler paragraph before the footnote destination.", 35));
        var section = new PdfSourceSection("Footnotes", "footnotes/index.html", $"{filler}\n\n{markdown}");
        var layout = new PdfLayout(new HtmlRenderer(), "Footnotes");
        layout.RenderSection(section);
        var result = layout.Complete();
        var contentHex = Convert.ToHexString(Encoding.ASCII.GetBytes("FootnoteContentQ7"));
        var contentPageIndex = result.Pages.Select((page, index) => (page, index))
            .Single(item => item.page.Content.ToString().Contains($"<{contentHex}> Tj", StringComparison.Ordinal)).index;
        var destination = Assert.Single(result.Destinations.Where(pair =>
            pair.Key.Contains("#fn:", StringComparison.Ordinal) &&
            !pair.Key.Contains("fnref:", StringComparison.Ordinal))).Value;

        Assert.True(contentPageIndex > 0);
        Assert.Equal(contentPageIndex, destination.PageIndex);

        PdfLayout.AddFooters(result.Pages);
        using var output = new MemoryStream();
        PdfFileWriter.Write(output, "Footnotes", result.Pages, result.Destinations);
        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("/Dest [", pdf);
    }

    [Fact]
    public void PreservesDirectionArrowsInPdfText()
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Arrows",
        [new PdfSourceSection("Arrows", "arrows/index.html", "Move → right and ← left.")], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("<2D3E> Tj", pdf);
        Assert.Contains("<3C2D> Tj", pdf);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(":custom_arrow_right:")), pdf);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(":custom_arrow_left:")), pdf);
    }

    [Fact]
    public void WritesEmojiSpanShortcodesAndKeepsArrowFallback()
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Emoji shortcodes",
        [new PdfSourceSection("Emoji shortcodes", "emoji-shortcodes/index.html", ":star: :o: :custom_arrow_right:")], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("<3A737461723A> Tj", pdf);
        Assert.Contains("<3A6F3A> Tj", pdf);
        Assert.Contains("<2D3E> Tj", pdf);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes(":custom_arrow_right:")), pdf);
    }

    [Fact]
    public void HandlesEmojiAtParagraphAndCodeWrappingBoundaries()
    {
        var paragraph = new string('p', 120) + "🚀";
        var code = new string('a', 93) + "🚀" + new string('b', 100);
        var markdown = $"{paragraph}\n\n```text\n{code}\n```";
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Wrapped emoji",
        [new PdfSourceSection("Wrapped emoji", "wrapped-emoji/index.html", markdown)], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains(Convert.ToHexString(Encoding.ASCII.GetBytes(":rocket:")), pdf);
        Assert.Equal("?", PdfEncoding.PrintableEmojiText("\ud83d"));
        Assert.Equal("3F", PdfEncoding.Hex("\ud83d"));
        var codeText = string.Concat(Regex.Matches(pdf,
                @"BT /F5 8\.5 Tf 1 0 0 1 [\d.]+ [\d.]+ Tm <(?<hex>[0-9A-F]*)> Tj ET")
            .Select(match => Encoding.Latin1.GetString(Convert.FromHexString(match.Groups["hex"].Value))));
        Assert.DoesNotContain(":rocket:", codeText);
        Assert.Contains("??", codeText);
    }

    [Fact]
    public void KeepsWrappedCodeTextInsideThePageMargins()
    {
        var code = new string('a', 90) + "→🚀" + new string('b', 110);
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Code width",
        [new PdfSourceSection("Code width", "code-width/index.html", $"```text\n{code}\n```")], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        var codeOperations = Regex.Matches(pdf,
            @"BT /F5 8\.5 Tf 1 0 0 1 (?<x>[\d.]+) [\d.]+ Tm <(?<hex>[0-9A-F]*)> Tj ET");
        Assert.NotEmpty(codeOperations);
        foreach (Match operation in codeOperations)
        {
            var x = double.Parse(operation.Groups["x"].Value, CultureInfo.InvariantCulture);
            var encodedLength = Convert.FromHexString(operation.Groups["hex"].Value).Length;
            var right = x + encodedLength * 8.5 * 0.6;
            Assert.True(right <= PdfLayout.PageWidth - PdfLayout.Right - 8 + 0.01,
                $"Code text reached {right:0.###} pt past the right edge.");
        }
    }

    [Fact]
    public void PlacesTableAnchorAtItsCellRow()
    {
        const string markdown = """
            <table>
            <tr><th>Header</th></tr>
            <tr><td>First row</td></tr>
            <tr><td><span id="later-row"></span>TargetTableCellQ7</td></tr>
            </table>
            """;
        var layout = new PdfLayout(new HtmlRenderer(), "Table anchors");
        layout.RenderSection(new PdfSourceSection("Table anchors", "table-anchors/index.html", markdown));
        var result = layout.Complete();
        var anchor = result.Destinations.Single(pair => pair.Key.EndsWith("#later-row", StringComparison.Ordinal)).Value;
        var contentHex = Convert.ToHexString(Encoding.ASCII.GetBytes("TargetTableCellQ7"));
        var content = result.Pages[anchor.PageIndex].Content.ToString();
        var contentOperation = Regex.Match(content,
            $@"1 0 0 1 [\d.]+ (?<y>[\d.]+) Tm <{contentHex}> Tj ET");
        Assert.True(contentOperation.Success);
        var textBaseline = double.Parse(contentOperation.Groups["y"].Value, CultureInfo.InvariantCulture);
        Assert.InRange(anchor.Top - textBaseline, 9.9, 10.1);
    }

    [Fact]
    public void WritesUnsupportedEmojiAsItsShortcode()
    {
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Emoji",
        [new PdfSourceSection("Emoji", "emoji/index.html", "Ship it 🚀.")], includeFrontMatter: false);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Contains("<3A726F636B65743A2E> Tj", pdf);
        Assert.DoesNotContain("3F3F", pdf);
    }

    [Fact]
    public void WritesMduContentToPdf17WithLinksAndVectors()
    {
        const string markdown = """
            # Guide

            A **bold** paragraph with [an external link](https://example.com) and a [local link](../Other.md#target).

            - [x] Completed
            - [ ] Pending

            ![Photo](https://example.com/photo.png)

            ```csharp
            var title = "Hello";
            ```

            > [!TIP]
            > Keep this note.

            ```mermaid
            flowchart TD
            A[Start] --> B[Finish]
            ```

            ```mermaid
            sequenceDiagram
            participant User
            participant Api
            User->>Api: Request
            alt Cached
            Api-->>User: Cached response
            else Fresh
            Api->>Api: Load data
            Api-->>User: Fresh response
            end
            ```

            $$
            x^2 + 1
            $$
            """;
        using var output = new MemoryStream();

        new PdfRenderer(new HtmlRenderer()).WritePdf(output, "Guide",
        [
            new PdfSourceSection("Guide", "guide/index.html", markdown),
            new PdfSourceSection("Other", "Other.html", "# Target\n\nDestination page.")
        ]);

        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.StartsWith("%PDF-1.7", pdf);
        Assert.Contains("<496E68616C74737665727A656963686E6973>", pdf);
        Assert.Contains("<4775696465>", pdf);
        Assert.Contains("/Subtype /Link", pdf);
        Assert.Contains(" /Annots [", pdf);
        Assert.Contains("/Dest [", pdf);
        Assert.Contains("<5B785D> Tj", pdf);
        Assert.Contains("<5B> Tj", pdf);
        Assert.Contains("<5D> Tj", pdf);
        Assert.Contains("https://example.com/photo.png", pdf);
        Assert.Contains(" c\n", pdf);
        Assert.Contains("xref\n", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }
}
