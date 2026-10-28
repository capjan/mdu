using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class PdfRendererTest
{
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
