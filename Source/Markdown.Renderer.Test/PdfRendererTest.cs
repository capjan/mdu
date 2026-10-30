using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class PdfRendererTest
{
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
