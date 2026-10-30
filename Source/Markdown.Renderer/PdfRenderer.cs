using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Renderer;

public sealed record PdfSourceSection(string Title, string RelativeHtmlPath, string Markdown, int Depth = 0);

/// <summary>Writes a paginated PDF from the HTML fragment produced by MDU's Markdown pipeline.</summary>
public sealed class PdfRenderer(HtmlRenderer htmlRenderer)
{
    public bool ContainsPdfLink(string markdown) => htmlRenderer.ContainsPdfLink(markdown);
    public IReadOnlyList<string> FindPdfLinks(string markdown) => htmlRenderer.FindPdfLinks(markdown);

    public void WritePdf(Stream output, string title, IReadOnlyList<PdfSourceSection> sections, bool includeFrontMatter = true)
    {
        var coverTitle = PdfEncoding.PrintableEmojiText(sections.FirstOrDefault()?.Title ?? title);
        var layout = new PdfLayout(htmlRenderer, coverTitle);
        foreach (var section in sections)
            layout.RenderSection(section);

        var result = layout.Complete();
        var description = sections.Count == 0 ? string.Empty : PdfEncoding.PrintableEmojiText(GetDescription(sections[0].Markdown));
        var frontMatter = includeFrontMatter ? PdfFrontMatter.Create(coverTitle, description, sections, result) :
            new PdfFrontMatterResult([]);
        var pages = frontMatter.Pages.Concat(result.Pages).ToArray();
        var destinations = result.Destinations.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with { PageIndex = pair.Value.PageIndex + frontMatter.Pages.Count },
            StringComparer.Ordinal);
        PdfLayout.AddFooters(pages);
        PdfFileWriter.Write(output, coverTitle, pages, destinations);
    }

    private string GetDescription(string markdown)
    {
        var html = PdfLayout.NormalizeVoidElements(htmlRenderer.RenderMarkdownFragment(markdown));
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml($"<root>{html}</root>");
        return PdfLayout.CollapseWhitespace(document.SelectSingleNode("//p")?.InnerText ?? string.Empty);
    }
}

internal sealed class PdfLayout
{
    private static readonly Regex StartTagRegex = new(@"<[a-zA-Z][^<>]*>", RegexOptions.Compiled);
    private static readonly Regex BooleanAttributeRegex = new(
        @"""[^""]*""|'[^']*'|\s(?<name>disabled|hidden|checked|selected|required|multiple|controls|playsinline|autoplay|muted|loop|data-media-error)(?=[\s/>])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    internal const double PageWidth = 595.28;
    internal const double PageHeight = 841.89;
    internal const double Left = 48;
    internal const double Right = 48;
    internal const double Top = 48;
    internal const double Bottom = 48;
    // Keep 17px HTML body text at the same physical size as 10.5pt PDF body text.
    private const double SvgCssPixelsToPdfPoints = 10.5 / 17.0;
    private const int ShortCodeLineLimit = 8;
    private readonly HtmlRenderer _htmlRenderer;
    private readonly string _documentTitle;
    private readonly List<PdfPageContent> _pages = [];
    private readonly Dictionary<string, PdfDestination> _destinations = new(StringComparer.Ordinal);
    private PdfPageContent _page;
    private double _y;
    private string _sectionPath = string.Empty;

    public PdfLayout(HtmlRenderer htmlRenderer, string documentTitle)
    {
        _htmlRenderer = htmlRenderer;
        _documentTitle = documentTitle;
        _page = NewPage();
        _y = Top;
    }

    public PdfLayoutResult Complete()
    {
        return new PdfLayoutResult(_pages, _destinations);
    }

    internal static void AddFooters(IReadOnlyList<PdfPageContent> pages)
    {
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var label = $"{index + 1} / {pages.Count}";
            page.Content.Append(ColorCommand(PdfColor.Secondary, fill: true)).Append("BT /F1 8 Tf 1 0 0 1 ")
                .Append(F(PageWidth - Right - TextWidth(label, 8, PdfFont.Helvetica))).Append(' ').Append(F(20)).Append(" Tm <")
                .Append(PdfEncoding.Hex(label)).Append("> Tj ET\n");
        }
    }

    public void RenderSection(PdfSourceSection section)
    {
        _sectionPath = section.RelativeHtmlPath.Replace('\\', '/');
        var html = NormalizeVoidElements(_htmlRenderer.RenderMarkdownFragment(section.Markdown));
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml($"<root>{html}</root>");
        var blocks = document.DocumentElement!.ChildNodes.Cast<XmlNode>().ToArray();
        if (_y > Top)
        {
            EnsureRoom(23 + MinimumStartHeight(blocks, 0));
            if (_y > Top)
            {
                _y += 10;
                DrawLine(Left, _y, PageWidth - Right, _y, PdfColor.LightBorder, 0.6);
                _y += 13;
            }
        }

        _destinations[_sectionPath] = new PdfDestination(_pages.Count - 1, PageHeight - _y);
        RenderBlocks(blocks, 0);
    }

    private void RenderBlocks(IEnumerable<XmlNode> nodes, int indent)
    {
        var blocks = nodes.ToArray();
        for (var index = 0; index < blocks.Length; index++)
        {
            if (blocks[index] is XmlElement heading && KeepsFollowingBlock(heading))
                EnsureRoom(MinimumStartHeight(blocks[index..], indent));
            RenderBlock(blocks[index], indent);
        }
    }

    private double MinimumBlockHeight(XmlNode? node, int indent)
    {
        if (node is not XmlElement element) return node is null ? 0 : 18;
        switch (element.LocalName.ToLowerInvariant())
        {
            case "p":
                var paragraphLines = ParagraphLines(element, 10.5, indent).Length;
                return 4 + (paragraphLines <= 3 ? paragraphLines : 2) * 10.5 * 1.35;
            case "pre":
                var codeLines = CodeLineCount(element, indent);
                return (codeLines <= ShortCodeLineLimit ? codeLines : 2) * 11 + 5;
            case "table":
                var (_, rows) = PrepareTable(element, indent);
                return TableStartHeight(rows);
            case "ul": case "ol":
                var firstItem = element.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.LocalName == "li");
                var itemLines = firstItem is null ? 0 : ParagraphLines(firstItem, 10.5, indent + 12).Length;
                return 2 + (itemLines <= 3 ? itemLines : 2) * 10.5 * 1.35;
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                return HeadingHeight(element, indent);
            case "figure" when HasClass(element, "diagram"):
                var diagram = DiagramSvg(element);
                return diagram is null ? 18 : SvgBlockHeight(diagram, 470, 300) + 10;
            case "div" when HasClass(element, "math-display"):
                var formula = element.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.LocalName == "svg");
                return formula is null ? 18 : SvgBlockHeight(formula, 470, 72) + 10;
            case "svg": return SvgBlockHeight(element, 470, 260) + 10;
            case "div": case "blockquote":
                return MinimumStartHeight(element.ChildNodes.Cast<XmlNode>().ToArray(),
                    indent + (element.LocalName == "blockquote" || HasClass(element, "markdown-alert") ? 12 : 0));
            default: return 18;
        }
    }

    private double MinimumStartHeight(IReadOnlyList<XmlNode> blocks, int indent)
    {
        var height = 0.0;
        foreach (var block in blocks)
        {
            if (block is not XmlElement && (block is not XmlText text || string.IsNullOrWhiteSpace(text.Value))) continue;
            if (block is XmlElement element && KeepsFollowingBlock(element))
                height += IsHeading(element) ? HeadingHeight(element, indent) :
                    4 + ParagraphLines(element, 10.5, indent).Length * 10.5 * 1.35 + 8;
            else
                return height + MinimumBlockHeight(block, indent);
        }
        return height;
    }

    private static bool KeepsFollowingBlock(XmlElement element) =>
        IsHeading(element) || HasClass(element, "markdown-alert-title");

    private List<PdfRun>[] ParagraphLines(XmlElement element, double size, int indent) =>
        WrapRuns(ReadInlineRuns(element.ChildNodes.Cast<XmlNode>(),
                IsHeading(element) ? PdfTextStyle.Heading(size) : PdfTextStyle.Body, _sectionPath),
            size, PageWidth - Left - Right - indent, mergeText: false);

    private double HeadingHeight(XmlElement heading, int indent)
    {
        var size = HeadingSize(heading);
        return 11 + ParagraphLines(heading, size, indent).Length * size * 1.35 + 5;
    }

    // Isometric diagrams wrap their SVG in phase controls, so the SVG is not always a direct child of the figure.
    // Its phases are cumulative and every element is drawn, which shows the last phase, where everything is visible.
    private static XmlElement? DiagramSvg(XmlElement figure) =>
        figure.GetElementsByTagName("svg").OfType<XmlElement>().FirstOrDefault();

    private static double SvgBlockHeight(XmlElement svg, double maxWidth, double maxHeight)
    {
        var dimensions = PdfSvgRenderer.Measure(svg);
        return dimensions.Height * SvgBlockScale(dimensions, maxWidth, maxHeight);
    }

    private static double SvgBlockScale(SvgDimensions dimensions, double maxWidth, double maxHeight) =>
        Math.Min(SvgCssPixelsToPdfPoints,
            Math.Min(maxWidth / Math.Max(dimensions.Width, 1), maxHeight / Math.Max(dimensions.Height, 1)));

    private static bool IsHeading(XmlElement element) =>
        element.LocalName.Length == 2 && element.LocalName[0] == 'h' && element.LocalName[1] is >= '1' and <= '6';

    private static double HeadingSize(XmlElement element) => (element.LocalName[1] - '0') switch
    {
        1 => 22, 2 => 17, 3 => 14, 4 => 12, _ => 11
    };

    private void RenderBlock(XmlNode node, int indent)
    {
        if (node is XmlText text)
        {
            if (!string.IsNullOrWhiteSpace(text.Value))
                WriteParagraph([new PdfRun(PdfEncoding.PrintableEmojiText(CollapseWhitespace(text.Value!)), PdfTextStyle.Body)], 10.5, indent, 4, 8);
            return;
        }

        if (node is not XmlElement element) return;
        var name = element.LocalName.ToLowerInvariant();
        switch (name)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                var headingSize = HeadingSize(element);
                var headingStyle = PdfTextStyle.Heading(headingSize);
                var headingRuns = ReadInlineRuns(element.ChildNodes.Cast<XmlNode>(), headingStyle, _sectionPath);
                if (element.HasAttribute("id"))
                    headingRuns.Insert(0, new PdfRun(string.Empty, headingStyle, Anchor: element.GetAttribute("id")));
                WriteParagraph(headingRuns, headingSize, indent, 11, 5, keepTogether: true);
                return;
            case "p":
                WriteParagraph(ReadInlineRuns(element.ChildNodes.Cast<XmlNode>(), PdfTextStyle.Body, _sectionPath),
                    10.5, indent, 4, 8, keepTogether: HasClass(element, "markdown-alert-title"));
                return;
            case "pre":
                RenderCode(element, indent);
                return;
            case "ul": case "ol":
                RenderList(element, indent + 12, name == "ol");
                return;
            case "table":
                RenderTable(element, indent);
                return;
            case "blockquote":
                EnsureRoom(MinimumBlockHeight(element, indent));
                var quotePage = _pages.Count - 1;
                var quoteTop = _y;
                RenderBlocks(element.ChildNodes.Cast<XmlNode>(), indent + 12);
                DrawAccentAcrossPages(quotePage, quoteTop, Left + indent + 3, PdfColor.Secondary, 2);
                _y += 4;
                return;
            case "button":
                return;
            case "hr":
                EnsureRoom(16);
                _y += 4;
                DrawLine(Left + indent, _y, PageWidth - Right, _y, PdfColor.LightBorder, 0.8);
                _y += 12;
                return;
            case "figure":
                if (HasClass(element, "diagram"))
                {
                    var svg = DiagramSvg(element);
                    if (svg is not null) DrawSvgBlock(svg, indent + 8, 470, 300);
                    return;
                }
                RenderBlocks(element.ChildNodes.Cast<XmlNode>(), indent);
                return;
            case "div":
                if (HasClass(element, "icon-search"))
                {
                    WriteParagraph([new PdfRun("[The interactive icon search is available in the HTML version.]", PdfTextStyle.Muted)], 9, indent + 8, 4, 8);
                    return;
                }
                if (HasClass(element, "markdown-alert"))
                {
                    EnsureRoom(MinimumBlockHeight(element, indent));
                    var alertPage = _pages.Count - 1;
                    var alertTop = _y;
                    RenderBlocks(element.ChildNodes.Cast<XmlNode>(), indent + 12);
                    var accent = HasClass(element, "markdown-alert-tip") ? new PdfColor(26, 127, 90) :
                        HasClass(element, "markdown-alert-important") ? new PdfColor(137, 68, 171) :
                        HasClass(element, "markdown-alert-warning") ? new PdfColor(154, 91, 0) :
                        HasClass(element, "markdown-alert-caution") ? new PdfColor(201, 37, 45) : PdfColor.Link;
                    DrawAccentAcrossPages(alertPage, alertTop, Left + indent + 3, accent, 2.5);
                    _y += 4;
                    return;
                }
                if (HasClass(element, "math-display"))
                {
                    var svg = element.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.LocalName == "svg");
                    if (svg is not null) DrawSvgBlock(svg, indent + 8, 470, 72);
                    else
                    {
                        var formula = element.GetAttribute("aria-label");
                        if (formula.Length > 0) WriteParagraph([new PdfRun(formula, PdfTextStyle.Math)], 10, indent + 8, 5, 8);
                    }
                    return;
                }
                if (HasClass(element, "media-embed"))
                {
                    var url = element.GetAttribute("data-embed-url");
                    WriteParagraph([new PdfRun(url.Length == 0 ? "[Externe Medien]" : url, PdfTextStyle.Linked(url))], 9.5, indent + 8, 4, 8);
                    return;
                }
                RenderBlocks(element.ChildNodes.Cast<XmlNode>(), indent);
                return;
            case "img":
                RenderImage(element, indent);
                return;
            case "video":
                RenderVideo(element, indent);
                return;
            case "svg":
                DrawSvgBlock(element, indent + 8, 470, 260);
                return;
            default:
                RenderBlocks(element.ChildNodes.Cast<XmlNode>(), indent);
                return;
        }
    }

    private void RenderList(XmlElement list, int indent, bool numbered)
    {
        var number = 1;
        foreach (XmlNode child in list.ChildNodes)
        {
            if (child is not XmlElement { LocalName: "li" } item) continue;
            var prefix = numbered ? $"{number++}. " : "• ";
            var runs = new List<PdfRun>();
            if (item.HasAttribute("id"))
                runs.Add(new PdfRun(string.Empty, PdfTextStyle.Body, Anchor: item.GetAttribute("id")));
            runs.Add(new PdfRun(prefix, PdfTextStyle.Body));
            foreach (XmlNode itemChild in item.ChildNodes)
            {
                if (itemChild is XmlElement { LocalName: "ul" or "ol" } nested)
                {
                    WriteParagraph(runs, 10.5, indent, 2, 2);
                    runs.Clear();
                    RenderList(nested, indent + 13, nested.LocalName == "ol");
                }
                else
                {
                    runs.AddRange(ReadInlineRuns([itemChild], PdfTextStyle.Body, _sectionPath));
                }
            }
            if (runs.Count > 1) WriteParagraph(runs, 10.5, indent, 2, 3);
        }
        _y += 3;
    }

    private void RenderCode(XmlElement pre, int indent)
    {
        var lines = WrapCodeLines(pre, indent);
        const double fontSize = 8.5;
        const double lineHeight = 11;
        var codeLeft = Left + indent + 8;
        var maxWidth = PageWidth - Right - codeLeft - 8;
        EnsureRoom((lines.Count <= ShortCodeLineLimit ? lines.Count : 2) * lineHeight + 5);
        foreach (var segments in lines)
        {
            EnsureRoom(lineHeight + 5);
            FillRect(codeLeft - 5, _y - 2, maxWidth + 10, lineHeight + 3, PdfColor.CodeBackground);
            var x = codeLeft;
            foreach (var segment in segments)
            {
                DrawText(segment.Text, x, _y + fontSize, fontSize, PdfFont.Courier, segment.Color);
                x += TextWidth(segment.Text, fontSize, PdfFont.Courier);
            }
            _y += lineHeight;
        }
        _y += 7;
    }

    private static List<List<(string Text, PdfColor Color)>> WrapCodeLines(XmlElement pre, int indent)
    {
        var codeRuns = new List<(string Text, PdfColor Color)>();
        ReadCodeRuns(pre, PdfColor.Text, codeRuns);
        var lines = new List<List<(string Text, PdfColor Color)>> { new() };
        foreach (var run in codeRuns)
        {
            var parts = run.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var part = 0; part < parts.Length; part++)
            {
                if (parts[part].Length > 0) lines[^1].Add((parts[part], run.Color));
                if (part < parts.Length - 1) lines.Add(new List<(string Text, PdfColor Color)>());
            }
        }
        if (lines.Count > 1 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        const double fontSize = 8.5;
        var maxWidth = PageWidth - Left - Right - indent - 16;
        var wrapped = new List<List<(string Text, PdfColor Color)>>();
        foreach (var segments in lines)
        {
            wrapped.Add([]);
            var width = 0.0;
            foreach (var segment in segments)
            {
                var remaining = segment.Text;
                while (remaining.Length > 0)
                {
                    var availableWidth = Math.Max(0, maxWidth - width);
                    var take = GetTextLengthWithinWidth(remaining, fontSize, availableWidth, PdfFont.Courier);
                    if (take == 0)
                    {
                        if (width > 0)
                        {
                            wrapped.Add([]);
                            width = 0;
                            continue;
                        }
                        take = TextElementLength(remaining, 0);
                    }
                    var text = remaining[..take];
                    wrapped[^1].Add((text, segment.Color));
                    width += TextWidth(text, fontSize, PdfFont.Courier);
                    remaining = remaining[take..];
                }
            }
        }
        return wrapped;
    }

    private static int CodeLineCount(XmlElement pre, int indent) => WrapCodeLines(pre, indent).Count;

    private static void ReadCodeRuns(XmlNode node, PdfColor color, List<(string Text, PdfColor Color)> runs)
    {
        if (node is XmlText text)
        {
            if (!string.IsNullOrEmpty(text.Value)) runs.Add((text.Value, color));
            return;
        }
        if (node is not XmlElement element) return;
        var className = element.GetAttribute("class");
        var childColor = className switch
        {
            "comment" => new PdfColor(56, 118, 29),
            "keyword" => new PdfColor(5, 80, 174),
            "string" => new PdfColor(163, 21, 21),
            "operator" => new PdfColor(5, 80, 174),
            _ => color
        };
        foreach (XmlNode child in element.ChildNodes) ReadCodeRuns(child, childColor, runs);
    }

    private void RenderTable(XmlElement table, int indent)
    {
        var (columnWidths, rows) = PrepareTable(table, indent);
        if (rows.Length == 0) return;
        var contentHeight = PageHeight - Top - Bottom;
        var headers = rows.TakeWhile(row => row.Cells.Any(cell => cell.Header)).ToArray();
        var headerHeight = headers.Sum(row => row.Height);
        var repeatHeaders = headerHeight + 30 < contentHeight;
        EnsureRoom(TableStartHeight(rows));

        foreach (var row in rows)
        {
            var line = 0;
            while (line < row.LineCount)
            {
                var remainingHeight = (row.LineCount - line) * 10.5 + 8;
                var availableHeight = PageHeight - Bottom - _y;
                var repeatingHeight = repeatHeaders && !headers.Contains(row) ? headerHeight : 0;
                // Large rows use the remaining space instead of leaving half a page empty.
                if (remainingHeight + 1 > availableHeight &&
                    (row.Height <= contentHeight / 2 && remainingHeight + repeatingHeight + 1 <= contentHeight ||
                     availableHeight < 30))
                    ContinueTable(row);

                var capacity = Math.Max(1, (int)Math.Floor((PageHeight - Bottom - _y - 9) / 10.5));
                var count = Math.Min(capacity, row.LineCount - line);
                if (row.LineCount - line - count == 1 && count > 2) count--;
                DrawRow(row, line, count);
                line += count;
                if (line < row.LineCount) ContinueTable(row);
            }
        }
        _y += 8;

        void ContinueTable(WrappedTableRow row)
        {
            StartPage();
            if (repeatHeaders && !headers.Contains(row))
                foreach (var header in headers) DrawRow(header, 0, header.LineCount, repeated: true);
        }

        void DrawRow(WrappedTableRow row, int firstLine, int count, bool repeated = false)
        {
            var height = count * 10.5 + 8;
            var width = columnWidths.Sum();
            if (row.Cells.Any(cell => cell.Header)) FillRect(Left + indent, _y, width, height, PdfColor.HeaderBackground);
            DrawLine(Left + indent, _y + height, Left + indent + width, _y + height, PdfColor.LightBorder, 0.5);
            var x = Left + indent + 5;
            for (var column = 0; column < columnWidths.Length; column++)
            {
                var header = column < row.Cells.Length && row.Cells[column].Header;
                for (var index = firstLine; index < Math.Min(firstLine + count, row.Lines[column].Length); index++)
                    DrawTableRuns(row.Lines[column][index], x, _y, index - firstLine, header, 8.5, !repeated);
                x += columnWidths[column];
            }
            _y += height;
        }
    }

    private (double[] ColumnWidths, WrappedTableRow[] Rows) PrepareTable(XmlElement table, int indent)
    {
        var rows = Descendants(table, "tr").Select(row => row.ChildNodes.OfType<XmlElement>()
            .Where(cell => cell.LocalName is "td" or "th")
            .Select(cell => new TableCell(ReadInlineRuns([cell], PdfTextStyle.Body, _sectionPath), cell.LocalName == "th"))
            .ToArray()).Where(row => row.Length > 0).ToArray();
        if (rows.Length == 0) return ([], []);

        var columns = rows.Max(row => row.Length);
        var width = PageWidth - Left - Right - indent;
        var columnWidths = Enumerable.Range(0, columns)
            .Select(column => Math.Max(50, width * (rows.Max(row => column < row.Length ? Math.Min(45, row[column].Text.Length) : 0) + 12) /
                Enumerable.Range(0, columns).Sum(index => rows.Max(row => index < row.Length ? Math.Min(45, row[index].Text.Length) : 0) + 12)))
            .ToArray();
        var totalWidth = columnWidths.Sum();
        if (totalWidth > width)
            for (var column = 0; column < columns; column++) columnWidths[column] *= width / totalWidth;

        var wrappedRows = rows.Select(row =>
        {
            var wrapped = new List<PdfRun>[columns][];
            for (var column = 0; column < columns; column++)
            {
                var cell = column < row.Length ? row[column] : new TableCell([], false);
                wrapped[column] = WrapRuns(cell.Runs, 8.5, columnWidths[column] - 10, cell.Header);
            }
            return new WrappedTableRow(row, wrapped);
        }).ToArray();
        return (columnWidths, wrappedRows);
    }

    private sealed record WrappedTableRow(TableCell[] Cells, List<PdfRun>[][] Lines)
    {
        public int LineCount => Lines.Max(column => column.Length);
        public double Height => LineCount * 10.5 + 8;
    }

    private static double TableStartHeight(WrappedTableRow[] rows)
    {
        var headers = rows.TakeWhile(row => row.Cells.Any(cell => cell.Header)).ToArray();
        var headerHeight = headers.Sum(row => row.Height);
        var firstHeight = rows.Skip(headers.Length).FirstOrDefault()?.Height ?? 0;
        var contentHeight = PageHeight - Top - Bottom;
        if (firstHeight > contentHeight / 2 || headerHeight + firstHeight + 1 > contentHeight)
            firstHeight = Math.Min(firstHeight, 29);
        return headerHeight + firstHeight + 1;
    }

    private static List<PdfRun>[] WrapRuns(IReadOnlyList<PdfRun> runs, double fontSize, double maxWidth,
        bool header = false, bool mergeText = true)
    {
        var lines = new List<List<PdfRun>> { new() };
        var width = 0.0;
        var pendingAnchors = new List<PdfRun>();
        foreach (var run in runs)
        {
            var style = TableStyle(run.Style, header);
            if (run.Anchor is not null)
                pendingAnchors.Add(new PdfRun(string.Empty, style, Anchor: run.Anchor));
            if (run.Svg is not null)
            {
                var dimensions = PdfSvgRenderer.Measure(run.Svg);
                var iconHeight = InlineIconHeight(fontSize);
                var iconWidth = iconHeight * dimensions.Width / Math.Max(1, dimensions.Height);
                if (width > 0 && width + iconWidth + 2 > maxWidth) NewLine();
                FlushAnchors();
                lines[^1].Add(new PdfRun(string.Empty, style, run.Svg));
                width += iconWidth + 2;
                continue;
            }

            foreach (var token in SplitInlineTokens(run.Text))
            {
                if (token.Contains('\n'))
                {
                    var pieces = token.Split('\n');
                    for (var piece = 0; piece < pieces.Length; piece++)
                    {
                        if (pieces[piece].Length > 0) AddWord(pieces[piece], style);
                        if (piece < pieces.Length - 1) NewLine();
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    if (width > 0 && lines[^1].LastOrDefault()?.Text.EndsWith(' ') != true)
                    {
                        if (width + TextWidth(" ", style.Size ?? fontSize, style.Font) <= maxWidth)
                            AddText(" ", style);
                    }
                    continue;
                }

                AddWord(token, style);
            }
        }
        FlushAnchors();
        return lines.ToArray();

        void AddWord(string word, PdfTextStyle style)
        {
            while (word.Length > 0)
            {
                var available = maxWidth - width;
                if (TextWidth(word, style.Size ?? fontSize, style.Font) <= available)
                {
                    AddText(word, style);
                    return;
                }
                if (width > 0)
                {
                    NewLine();
                    continue;
                }

                var pieces = WrapText(word, style.Size ?? fontSize, maxWidth, style.Font).ToArray();
                for (var index = 0; index < pieces.Length; index++)
                {
                    AddText(pieces[index], style);
                    if (index < pieces.Length - 1) NewLine();
                }
                return;
            }
        }

        void AddText(string text, PdfTextStyle style)
        {
            if (!string.IsNullOrWhiteSpace(text)) FlushAnchors();
            if (mergeText && lines[^1].Count > 0 && lines[^1][^1].Svg is null && lines[^1][^1].Style == style)
                lines[^1][^1] = lines[^1][^1] with { Text = lines[^1][^1].Text + text };
            else
                lines[^1].Add(new PdfRun(text, style));
            width += TextWidth(text, style.Size ?? fontSize, style.Font);
        }

        void NewLine()
        {
            lines.Add([]);
            width = 0;
        }

        void FlushAnchors()
        {
            lines[^1].AddRange(pendingAnchors);
            pendingAnchors.Clear();
        }
    }

    private void DrawTableRuns(IReadOnlyList<PdfRun> runs, double x, double rowTop, int line, bool header, double fontSize,
        bool markAnchors = true)
    {
        var top = rowTop + line * 10.5;
        var baseline = top + 10;
        foreach (var run in runs)
        {
            var style = TableStyle(run.Style, header);
            if (markAnchors && run.Anchor is not null) MarkAnchor(run.Anchor, top);
            if (run.Svg is not null)
            {
                var dimensions = PdfSvgRenderer.Measure(run.Svg);
                x += DrawInlineSvg(run.Svg, dimensions, x, top, 10.5, InlineIconHeight(fontSize));
                continue;
            }
            if (run.Text.Length == 0) continue;
            var size = style.Size ?? 8.5;
            x += DrawStyledText(run.Text, style, x, top, baseline, size, 10.5, 9.5);
        }
    }

    private static PdfTextStyle TableStyle(PdfTextStyle style, bool header) => !header ? style : style with
    {
        Font = style.Font switch
        {
            PdfFont.Helvetica => PdfFont.HelveticaBold,
            PdfFont.HelveticaItalic => PdfFont.HelveticaBoldItalic,
            _ => style.Font
        }
    };

    private void RenderImage(XmlElement image, int indent)
    {
        var alt = image.GetAttribute("alt");
        var source = image.GetAttribute("src");
        var text = alt.Length == 0 ? "[Bild]" : $"[Bild: {alt}]";
        WriteParagraph([new PdfRun(text, PdfTextStyle.Muted)], 9, indent + 8, 4, 7);
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            WriteParagraph([new PdfRun(source, PdfTextStyle.Linked(source))], 8.5, indent + 8, 0, 7);
    }

    private void RenderVideo(XmlElement video, int indent)
    {
        var source = video.ChildNodes.OfType<XmlElement>()
            .Where(child => child.LocalName == "source")
            .Select(child => child.GetAttribute("src"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? video.GetAttribute("src");
        var label = source.Length == 0 ? "[Video]" : "[Watch video]";
        var style = source.Length == 0 ? PdfTextStyle.Muted : PdfTextStyle.Linked(source);
        WriteParagraph([new PdfRun(label, style)], 9, indent + 8, 4, 7);
    }

    private void DrawSvgBlock(XmlElement svg, int indent, double maxWidth, double maxHeight)
    {
        var size = PdfSvgRenderer.Measure(svg);
        var scale = SvgBlockScale(size, maxWidth, maxHeight);
        var width = size.Width * scale;
        var height = size.Height * scale;
        EnsureRoom(height + 10);
        PdfSvgRenderer.Draw(svg, _page.Content, Left + indent + Math.Max(0, (maxWidth - width) / 2), _y,
            width, height, PageHeight, AddSvgLink);
        _y += height + 8;
    }

    private List<PdfRun> ReadInlineRuns(IEnumerable<XmlNode> nodes, PdfTextStyle style, string sectionPath)
    {
        var runs = new List<PdfRun>();
        foreach (XmlNode node in nodes) ReadInline(node, style, sectionPath, runs);
        return runs;
    }

    private void ReadInline(XmlNode node, PdfTextStyle style, string sectionPath, List<PdfRun> runs)
    {
        if (node is XmlText text)
        {
            if (text.Value?.Length > 0)
                runs.Add(new PdfRun(PdfEncoding.PrintableEmojiText(NormalizeInlineWhitespace(text.Value)), style));
            return;
        }
        if (node is not XmlElement element) return;
        if (element.HasAttribute("id"))
            runs.Add(new PdfRun(string.Empty, style, Anchor: element.GetAttribute("id")));
        var name = element.LocalName.ToLowerInvariant();
        if (name == "br")
        {
            runs.Add(new PdfRun("\n", style));
            return;
        }
        if (name == "button" || HasClass(element, "key-symbol")) return;
        if (name == "img")
        {
            var alt = element.GetAttribute("alt");
            runs.Add(new PdfRun(alt.Length == 0 ? "[Bild]" : $"[Bild: {alt}]", PdfTextStyle.Muted));
            var source = element.GetAttribute("src");
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                runs.Add(new PdfRun($" ({source})", PdfTextStyle.Linked(source)));
            return;
        }
        if (name == "input" && element.GetAttribute("type") == "checkbox")
        {
            runs.Add(new PdfRun(element.HasAttribute("checked") ? "[x] " : "[ ] ", style));
            return;
        }
        if (name == "svg")
        {
            runs.Add(new PdfRun(string.Empty, style, element));
            return;
        }
        if (name == "span" && HasClass(element, "emoji"))
        {
            var emoji = CollapseWhitespace(element.InnerText);
            var shortcode = element.GetAttribute("title");
            runs.Add(new PdfRun(PdfEncoding.PrintableEmojiSpan(emoji, shortcode), style));
            return;
        }

        var childStyle = name switch
        {
            "strong" or "b" => style with { Font = style.Font switch { PdfFont.Helvetica => PdfFont.HelveticaBold, PdfFont.HelveticaItalic => PdfFont.HelveticaBoldItalic, _ => PdfFont.CourierBold } },
            "em" or "i" => style with { Font = style.Font == PdfFont.Helvetica ? PdfFont.HelveticaItalic : style.Font },
            "code" or "kbd" => style with { Font = PdfFont.Courier, Background = PdfColor.CodeBackground },
            "a" => style with { Color = PdfColor.Link, Underline = true, Link = element.GetAttribute("href") },
            "del" or "s" => style with { Strike = true },
            _ => style
        };
        foreach (XmlNode child in element.ChildNodes) ReadInline(child, childStyle, sectionPath, runs);
    }

    private void WriteParagraph(IReadOnlyList<PdfRun> runs, double fontSize, int indent, double before, double after,
        bool keepTogether = false)
    {
        if (runs.All(run => run.Svg is null && string.IsNullOrWhiteSpace(run.Text)))
        {
            _y += before;
            foreach (var run in runs)
                if (run.Anchor is not null) MarkAnchor(run.Anchor);
            _y += after;
            return;
        }
        var lines = WrapRuns(runs, fontSize, PageWidth - Left - Right - indent, mergeText: false);
        var lineHeight = fontSize * 1.35;
        var minimumLines = keepTogether || lines.Length <= 3 ? lines.Length : 2;
        EnsureRoom((_y > Top ? before : 0) + minimumLines * lineHeight);
        if (_y > Top) _y += before;
        var line = 0;
        while (line < lines.Length)
        {
            var capacity = (int)Math.Floor((PageHeight - Bottom - _y + 0.001) / lineHeight);
            if (capacity < Math.Min(2, lines.Length - line))
            {
                StartPage();
                continue;
            }
            if (lines.Length - line == 3 && capacity == 2)
            {
                StartPage();
                continue;
            }
            var count = Math.Min(capacity, lines.Length - line);
            if (lines.Length - line - count == 1 && count > 1) count--;
            for (var index = 0; index < count; index++, line++)
            {
                var x = Left + indent;
                foreach (var run in lines[line])
                {
                    if (run.Anchor is not null) MarkAnchor(run.Anchor);
                    if (run.Svg is not null)
                    {
                        x += DrawInlineSvg(run.Svg, PdfSvgRenderer.Measure(run.Svg), x, _y, lineHeight, InlineIconHeight(fontSize));
                        continue;
                    }
                    var size = run.Style.Size ?? fontSize;
                    x += DrawStyledText(run.Text, run.Style, x, _y, _y + size * 0.95, size, lineHeight);
                }
                _y += lineHeight;
            }
        }
        _y += after;
    }

    private double DrawStyledText(
        string text,
        PdfTextStyle style,
        double x,
        double top,
        double baseline,
        double size,
        double lineHeight,
        double? backgroundHeight = null)
    {
        var width = TextWidth(text, size, style.Font);
        if (style.Background is { } background)
            FillRect(x - 1, top + 1, width + 2, backgroundHeight ?? lineHeight - 2, background);
        DrawText(text, x, baseline, size, style.Font, style.Color);
        if (style.Underline) DrawLine(x, baseline + 1.4, x + width, baseline + 1.4, style.Color, 0.45);
        if (style.Strike) DrawLine(x, baseline - size * 0.3, x + width, baseline - size * 0.3, style.Color, 0.45);
        if (style.Link is { Length: > 0 } link)
        {
            var pdfLink = CreateLink(link, x, PageHeight - (top + lineHeight), x + width, PageHeight - top);
            if (pdfLink is not null) _page.Links.Add(pdfLink);
        }
        return width;
    }

    private double DrawInlineSvg(XmlElement svg, SvgDimensions dimensions, double x, double top, double lineHeight, double iconHeight)
    {
        var iconWidth = iconHeight * dimensions.Width / Math.Max(1, dimensions.Height);
        PdfSvgRenderer.Draw(svg, _page.Content, x, top + (lineHeight - iconHeight) / 2,
            iconWidth, iconHeight, PageHeight, AddSvgLink);
        return iconWidth + 2;
    }

    private static double InlineIconHeight(double fontSize) => fontSize * 0.9;

    private static IEnumerable<string> SplitInlineTokens(string text) =>
        Regex.Split(text, "(\\s+)").Where(token => token.Length > 0);

    private void MarkAnchor(string id, double? top = null) =>
        _destinations[$"{_sectionPath}#{id}"] = new PdfDestination(_pages.Count - 1, PageHeight - (top ?? _y));

    private PdfLink? CreateLink(string href, double left, double bottom, double right, double top)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            return new PdfLink(left, bottom, right, top, uri.AbsoluteUri, null);

        var baseUri = new Uri($"https://mdu.local/{Uri.EscapeDataString(_sectionPath).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}");
        if (!Uri.TryCreate(baseUri, href, out var target) || target.Host != "mdu.local") return null;
        var path = Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/'));
        var fragment = Uri.UnescapeDataString(target.Fragment.TrimStart('#'));
        var destination = fragment.Length == 0 ? path : $"{path}#{fragment}";
        return new PdfLink(left, bottom, right, top, null, destination);
    }

    private void AddSvgLink(string href, double left, double bottom, double right, double top)
    {
        var link = CreateLink(href, left, bottom, right, top);
        if (link is not null) _page.Links.Add(link);
    }

    private void EnsureRoom(double height)
    {
        if (_y <= Top || _y + height <= PageHeight - Bottom) return;
        StartPage();
    }

    private void StartPage()
    {
        _page = NewPage();
        _y = Top;
    }

    private PdfPageContent NewPage()
    {
        var page = new PdfPageContent(PageWidth, PageHeight);
        if (_documentTitle.Length > 0)
        {
            var header = WrapText(_documentTitle, 8, PageWidth - Left - Right, PdfFont.HelveticaBold).FirstOrDefault() ?? string.Empty;
            page.Content.Append(ColorCommand(PdfColor.Secondary, fill: true)).Append("BT /F2 8 Tf 1 0 0 1 ")
                .Append(F(Left)).Append(' ').Append(F(PageHeight - 27)).Append(" Tm <")
                .Append(PdfEncoding.Hex(header)).Append("> Tj ET\n")
                .Append(ColorCommand(PdfColor.LightBorder, fill: false)).Append("0.5 w ")
                .Append(F(Left)).Append(' ').Append(F(PageHeight - 36)).Append(" m ")
                .Append(F(PageWidth - Right)).Append(' ').Append(F(PageHeight - 36)).Append(" l S\n");
        }
        _pages.Add(page);
        return page;
    }

    private void DrawText(string text, double x, double baselineTop, double size, PdfFont font, PdfColor color)
    {
        var y = PageHeight - baselineTop;
        _page.Content.Append(ColorCommand(color, fill: true)).Append("BT /").Append(PdfFileWriter.FontResource(font))
            .Append(' ').Append(F(size)).Append(" Tf 1 0 0 1 ").Append(F(x)).Append(' ').Append(F(y))
            .Append(" Tm <").Append(PdfEncoding.Hex(text)).Append("> Tj ET\n");
    }

    private void FillRect(double x, double top, double width, double height, PdfColor color) =>
        _page.Content.Append(ColorCommand(color, fill: true)).Append(F(x)).Append(' ')
            .Append(F(PageHeight - top - height)).Append(' ').Append(F(width)).Append(' ').Append(F(height)).Append(" re f\n");

    private void DrawLine(double x1, double top1, double x2, double top2, PdfColor color, double width) =>
        _page.Content.Append(ColorCommand(color, fill: false)).Append(F(width)).Append(" w ")
            .Append(F(x1)).Append(' ').Append(F(PageHeight - top1)).Append(" m ")
            .Append(F(x2)).Append(' ').Append(F(PageHeight - top2)).Append(" l S\n");

    private void DrawAccentAcrossPages(int startPage, double startTop, double x, PdfColor color, double width)
    {
        var lastPage = _pages.Count - 1;
        for (var pageIndex = startPage; pageIndex <= lastPage; pageIndex++)
        {
            var top = pageIndex == startPage ? startTop : Top;
            var bottom = pageIndex == lastPage ? _y - 2 : PageHeight - Bottom;
            if (bottom <= top) continue;
            var page = _pages[pageIndex];
            page.Content.Append(ColorCommand(color, fill: false)).Append(F(width)).Append(" w ")
                .Append(F(x)).Append(' ').Append(F(PageHeight - top)).Append(" m ")
                .Append(F(x)).Append(' ').Append(F(PageHeight - bottom)).Append(" l S\n");
        }
    }

    internal static string ColorCommand(PdfColor color, bool fill) =>
        $"{F(color.R / 255.0)} {F(color.G / 255.0)} {F(color.B / 255.0)} {(fill ? "rg" : "RG")} ";

    internal static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool HasClass(XmlElement element, string name) =>
        element.GetAttribute("class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
    internal static string CollapseWhitespace(string text) => Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    private static string NormalizeInlineWhitespace(string text) => Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ");
    internal static string NormalizeVoidElements(string html)
    {
        var normalized = Regex.Replace(html, @"<(input|br|hr|img|source)(?<attributes>[^<>]*?)(?<!/)>", "<$1${attributes} />", RegexOptions.IgnoreCase);
        return StartTagRegex.Replace(normalized, tag =>
            BooleanAttributeRegex.Replace(tag.Value, match =>
            {
                var name = match.Groups["name"].Value;
                return name.Length == 0 ? match.Value : $" {name}=\"{name}\"";
            }));
    }
    private static IEnumerable<XmlElement> Descendants(XmlElement element, string name) =>
        element.GetElementsByTagName(name).OfType<XmlElement>();

    internal static IEnumerable<string> WrapText(string text, double size, double maxWidth, PdfFont font)
    {
        var line = new StringBuilder();
        foreach (var token in Regex.Split(text, "(?<=\\s)").Where(token => token.Length > 0))
        {
            var word = token;
            if (line.Length > 0 && TextWidth(line.ToString() + word, size, font) > maxWidth)
            {
                yield return line.ToString().TrimEnd();
                line.Clear();
            }
            while (word.Length > 0 && TextWidth(line.ToString() + word, size, font) > maxWidth)
            {
                var take = GetTextLengthWithinWidth(word, size, maxWidth, font);
                if (take == 0) take = TextElementLength(word, 0);
                yield return word[..take];
                word = word[take..];
            }
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString().TrimEnd();
    }

    internal static double TextWidth(string text, double size, PdfFont font)
    {
        var widths = font is PdfFont.HelveticaBold or PdfFont.HelveticaBoldItalic
            ? HelveticaBoldWidths
            : HelveticaWidths;
        var width = 0.0;
        foreach (var character in text)
        {
            if (PdfEncoding.FallbackText(character) is { } fallback)
            {
                foreach (var fallbackCharacter in fallback) width += CharacterWidth(fallbackCharacter);
            }
            else
            {
                width += CharacterWidth(character);
            }
        }
        return width * size;

        double CharacterWidth(char character)
        {
            if (font is PdfFont.Courier or PdfFont.CourierBold) return 0.6;
            var baseCharacter = ToBaseCharacter(character);
            return (baseCharacter is >= ' ' and <= '~' ? widths[baseCharacter - ' '] : widths['?' - ' ']) / 1000.0;
        }
    }

    private static int GetTextLengthWithinWidth(string text, double size, double maxWidth, PdfFont font)
    {
        var width = 0.0;
        var length = 0;
        while (length < text.Length)
        {
            var elementLength = TextElementLength(text, length);
            var elementWidth = TextWidth(text.Substring(length, elementLength), size, font);
            if (width + elementWidth > maxWidth) break;
            width += elementWidth;
            length += elementLength;
        }
        return length;
    }

    private static int TextElementLength(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

    private static char ToBaseCharacter(char character) => character switch
    {
        'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' => 'A',
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
        'Ç' => 'C', 'ç' => 'c',
        'È' or 'É' or 'Ê' or 'Ë' => 'E',
        'è' or 'é' or 'ê' or 'ë' => 'e',
        'Ì' or 'Í' or 'Î' or 'Ï' => 'I',
        'ì' or 'í' or 'î' or 'ï' => 'i',
        'Ñ' => 'N', 'ñ' => 'n',
        'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' => 'O',
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' => 'o',
        'Ù' or 'Ú' or 'Û' or 'Ü' => 'U',
        'ù' or 'ú' or 'û' or 'ü' => 'u',
        _ => character
    };

    // Standard Helvetica widths from PDF's 1000-unit text space, used for precise line layout.
    private static readonly int[] HelveticaWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 611, 556, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    private static readonly int[] HelveticaBoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    ];
}

internal sealed class PdfPageContent(double width, double height)
{
    public double Width { get; } = width;
    public double Height { get; } = height;
    public StringBuilder Content { get; } = new();
    public List<PdfLink> Links { get; } = [];
}

internal sealed record PdfLink(double Left, double Bottom, double Right, double Top, string? Uri, string? Destination);
internal sealed record PdfDestination(int PageIndex, double Top);
internal sealed record PdfLayoutResult(IReadOnlyList<PdfPageContent> Pages, IReadOnlyDictionary<string, PdfDestination> Destinations);
internal sealed record TableCell(IReadOnlyList<PdfRun> Runs, bool Header)
{
    public string Text { get; } = string.Concat(Runs.Select(run => run.Svg is null ? run.Text : "icon"));
}
internal sealed record PdfRun(string Text, PdfTextStyle Style, XmlElement? Svg = null, string? Anchor = null);
internal sealed record PdfTextStyle(PdfFont Font, PdfColor Color, double? Size = null, PdfColor? Background = null, bool Underline = false, bool Strike = false, string? Link = null)
{
    public static PdfTextStyle Body { get; } = new(PdfFont.Helvetica, PdfColor.Text);
    public static PdfTextStyle Heading(double size) => new(PdfFont.HelveticaBold, PdfColor.Text, size);
    public static PdfTextStyle Muted { get; } = new(PdfFont.Helvetica, PdfColor.Secondary);
    public static PdfTextStyle Math { get; } = new(PdfFont.HelveticaItalic, PdfColor.Text);
    public static PdfTextStyle Linked(string uri) => new(PdfFont.Helvetica, PdfColor.Link, Link: uri, Underline: true);
}

internal enum PdfFont { Helvetica, HelveticaBold, HelveticaItalic, HelveticaBoldItalic, Courier, CourierBold }
internal readonly record struct PdfColor(byte R, byte G, byte B)
{
    public static PdfColor Text => new(29, 29, 31);
    public static PdfColor Secondary => new(110, 110, 115);
    public static PdfColor Link => new(0, 102, 204);
    public static PdfColor LightBorder => new(220, 220, 225);
    public static PdfColor CodeBackground => new(245, 245, 247);
    public static PdfColor HeaderBackground => new(239, 242, 247);
}

internal static class PdfEncoding
{
    private static readonly IReadOnlyDictionary<int, (string Unicode, string Shortcode)[]> EmojiByFirstRune =
        ShortcodeExtension.Emojis
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(group => (Unicode: group.Key, Shortcode: group.First().Key))
            .Where(emoji => IsEmoji(emoji.Unicode))
            .GroupBy(emoji => FirstRuneValue(emoji.Unicode))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(emoji => emoji.Unicode.Length).ToArray());
    private static readonly Dictionary<char, byte> SpecialCharacters = new()
    {
        ['\u2013'] = (byte)'-', ['\u2014'] = (byte)'-', ['\u2011'] = (byte)'-', ['\u2018'] = 0x91, ['\u2019'] = 0x92,
        ['\u201c'] = 0x93, ['\u201d'] = 0x94, ['\u2022'] = 0x95, ['\u2026'] = 0x85,
        ['\u00a0'] = 0xa0, ['\u2122'] = 0x99
    };

    public static string Hex(string value)
    {
        var bytes = new List<byte>();
        foreach (var character in value)
        {
            if (character <= 0xff && !char.IsControl(character)) bytes.Add((byte)character);
            else if (SpecialCharacters.TryGetValue(character, out var encoded)) bytes.Add(encoded);
            else if (FallbackText(character) is { } fallback)
                foreach (var fallbackCharacter in fallback) bytes.Add((byte)fallbackCharacter);
            else if (!char.IsControl(character)) bytes.Add((byte)'?');
        }
        return Convert.ToHexString(bytes.ToArray());
    }

    internal static string? FallbackText(char character) => character switch
    {
        '\u2013' or '\u2014' or '\u2011' => "-",
        '\u2192' => "->",
        '\u2190' => "<-",
        '\u2191' => "Up",
        '\u2193' => "Down",
        '\u2318' => "Cmd",
        '\u2325' => "Opt",
        '\u2303' => "Ctrl",
        '\u21e7' => "Shift",
        '\u21a9' => "Return",
        '\u232b' => "Backspace",
        '\u2326' => "Delete",
        '\u2713' or '\u2714' => "[x]",
        '\u2610' => "[ ]",
        _ => null
    };

    internal static string PrintableEmojiText(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune))
            {
                result.Append('?');
                index++;
                continue;
            }
            if (EmojiByFirstRune.TryGetValue(rune.Value, out var candidates))
            {
                var emoji = candidates.FirstOrDefault(candidate => value.AsSpan(index).StartsWith(candidate.Unicode, StringComparison.Ordinal));
                if (emoji.Unicode is not null)
                {
                    result.Append(emoji.Shortcode);
                    index += emoji.Unicode.Length;
                    continue;
                }
            }

            result.Append(rune.ToString());
            index += rune.Utf16SequenceLength;
        }
        return result.ToString();
    }

    internal static string PrintableEmojiSpan(string value, string shortcode)
    {
        if (ShortcodeExtension.Emojis.TryGetValue(shortcode, out var emoji) &&
            string.Equals(value, emoji, StringComparison.Ordinal) &&
            !value.Any(character => FallbackText(character) is not null))
            return shortcode;

        return PrintableEmojiText(value);
    }

    private static bool IsEmoji(string value)
    {
        if (value.Contains('\ufe0f') || value.Contains('\u20e3')) return true;
        if (!Rune.TryGetRuneAt(value, 0, out var rune)) return false;
        // Keep text-presentation arrows and other symbols unchanged while mapping emoji glyphs.
        return rune.Value is >= 0x1f000 and <= 0x1faff or
            >= 0x231a and <= 0x231b or
            >= 0x23e9 and <= 0x23ec or
            0x23f0 or 0x23f3 or
            >= 0x25fd and <= 0x25fe or
            >= 0x2614 and <= 0x2615 or
            >= 0x2648 and <= 0x2653 or
            0x267f or 0x2693 or 0x26a1 or
            >= 0x26aa and <= 0x26ab or
            >= 0x26bd and <= 0x26be or
            >= 0x26c4 and <= 0x26c5 or
            0x26ce or 0x26d4 or 0x26ea or
            >= 0x26f2 and <= 0x26f3 or
            0x26f5 or 0x26fa or 0x26fd or 0x2705 or
            >= 0x270a and <= 0x270b or
            0x2728 or 0x274c or 0x274e or
            >= 0x2753 and <= 0x2755 or
            0x2757 or
            >= 0x2795 and <= 0x2797 or
            0x27b0 or 0x27bf;
    }

    private static int FirstRuneValue(string value) =>
        Rune.TryGetRuneAt(value, 0, out var rune) ? rune.Value : 0;
}

internal static class PdfFileWriter
{
    private static readonly (PdfFont Font, string Name)[] Fonts =
    [
        (PdfFont.Helvetica, "Helvetica"), (PdfFont.HelveticaBold, "Helvetica-Bold"),
        (PdfFont.HelveticaItalic, "Helvetica-Oblique"), (PdfFont.HelveticaBoldItalic, "Helvetica-BoldOblique"),
        (PdfFont.Courier, "Courier"), (PdfFont.CourierBold, "Courier-Bold")
    ];

    public static string FontResource(PdfFont font) => font switch
    {
        PdfFont.Helvetica => "F1", PdfFont.HelveticaBold => "F2", PdfFont.HelveticaItalic => "F3",
        PdfFont.HelveticaBoldItalic => "F4", PdfFont.Courier => "F5", _ => "F6"
    };

    public static void Write(
        Stream output,
        string title,
        IReadOnlyList<PdfPageContent> pages,
        IReadOnlyDictionary<string, PdfDestination> destinations)
    {
        var objects = new List<byte[]?> { null };
        int Reserve()
        {
            objects.Add(null);
            return objects.Count - 1;
        }
        int Add(string value)
        {
            objects.Add(Encoding.Latin1.GetBytes(value));
            return objects.Count - 1;
        }
        int AddStream(byte[] stream, string extraDictionary = "")
        {
            var prefix = Encoding.ASCII.GetBytes($"<< /Length {stream.Length}{extraDictionary} >>\nstream\n");
            var suffix = Encoding.ASCII.GetBytes("\nendstream");
            var bytes = new byte[prefix.Length + stream.Length + suffix.Length];
            prefix.CopyTo(bytes, 0);
            stream.CopyTo(bytes, prefix.Length);
            suffix.CopyTo(bytes, prefix.Length + stream.Length);
            objects.Add(bytes);
            return objects.Count - 1;
        }

        var catalogId = Reserve();
        var pagesId = Reserve();
        var fontIds = Fonts.Select(font => Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{font.Name} /Encoding /WinAnsiEncoding >>")).ToArray();
        var pageIds = pages.Select(_ => Reserve()).ToArray();
        var contentIds = pages.Select(page => AddStream(Encoding.Latin1.GetBytes(page.Content.ToString()))).ToArray();
        var annotationIds = new int[pages.Count][];
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];
            annotationIds[pageIndex] = page.Links.Select(link =>
            {
                var rectangle = $"[{F(link.Left)} {F(link.Bottom)} {F(link.Right)} {F(link.Top)}]";
                if (link.Uri is not null)
                    return Add($"<< /Type /Annot /Subtype /Link /Rect {rectangle} /Border [0 0 0] /A << /S /URI /URI ({PdfString(link.Uri)}) >> >>");
                if (link.Destination is not null && destinations.TryGetValue(link.Destination, out var destination) &&
                    destination.PageIndex >= 0 && destination.PageIndex < pageIds.Length)
                    return Add($"<< /Type /Annot /Subtype /Link /Rect {rectangle} /Border [0 0 0] /Dest [{pageIds[destination.PageIndex]} 0 R /XYZ 0 {F(destination.Top)} null] >>");
                return 0;
            }).Where(id => id > 0).ToArray();
        }

        for (var index = 0; index < pages.Count; index++)
        {
            var annots = annotationIds[index].Length == 0 ? string.Empty : $" /Annots [{string.Join(' ', annotationIds[index].Select(id => $"{id} 0 R"))}]";
            objects[pageIds[index]] = Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {F(pages[index].Width)} {F(pages[index].Height)}] /Resources << /Font << {string.Join(' ', fontIds.Select((id, font) => $"/{FontResource(Fonts[font].Font)} {id} 0 R"))} >> >> /Contents {contentIds[index]} 0 R{annots} >>");
        }
        objects[pagesId] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Length} >>");
        objects[catalogId] = Encoding.ASCII.GetBytes($"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        var infoId = Add($"<< /Title ({PdfString(title)}) /Producer (MDU) >>");

        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.Latin1.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n"));
        var offsets = new long[objects.Count];
        for (var id = 1; id < objects.Count; id++)
        {
            offsets[id] = output.Position;
            writer.Write(Encoding.ASCII.GetBytes($"{id} 0 obj\n"));
            writer.Write(objects[id] ?? throw new InvalidOperationException("A PDF object was not written."));
            writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }
        var xrefOffset = output.Position;
        writer.Write(Encoding.ASCII.GetBytes($"xref\n0 {objects.Count}\n0000000000 65535 f \n"));
        for (var id = 1; id < objects.Count; id++)
            writer.Write(Encoding.ASCII.GetBytes($"{offsets[id]:D10} 00000 n \n"));
        writer.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objects.Count} /Root {catalogId} 0 R /Info {infoId} 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));
    }

    private static string PdfString(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
