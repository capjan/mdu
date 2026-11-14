using System.Globalization;
using StaticCode.Icons;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html;
using AngleSharp.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Search;

public sealed class SearchIndexBuilder
{
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex InlineMarkdownLinkRegex = new(
        @"(?<![\p{L}\p{N}_])\[(?<label>[^\[\]]+)\]\((?:[^()]|\([^()]*\))*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly SearchIndexJsonContext JsonContext = new(JsonOptions);

    private readonly List<SearchIndexEntry> _entries = new();
    private readonly Dictionary<IconGlyph, string> _iconText = new();
    private readonly string _codePrefix = $"mdu-search-code-{Guid.NewGuid():N}:";
    private HtmlParser? _htmlParser;
    private IElement? _svgContext;

    private HtmlParser HtmlParser => _htmlParser ??= new HtmlParser(new HtmlParserOptions { IsScripting = true });
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseGridTables()
        .UsePipeTables()
        .UseAdvancedExtensions()
        .UseHeadingIds()
        .UseGitHubAlerts()
        .UseIconsAndEmojis()
        .UseKeyboardKeys()
        .Build();

    public void Clear()
    {
        _entries.Clear();
        _iconText.Clear();
    }

    /// <param name="title">The page title.</param>
    /// <param name="url">The page URL relative to the site.</param>
    /// <param name="markdown">The page without front matter.</param>
    /// <param name="tags">The tag names of the page.</param>
    /// <param name="codeAnnotations">Whether the page's code blocks show annotations: its front matter, or else the site's setting.</param>
    public void AddPage(string title, string url, string markdown, IReadOnlyList<string>? tags = null, bool codeAnnotations = true)
    {
        _entries.RemoveAll(entry => entry.PageUrl.Equals(url, StringComparison.Ordinal));
        var firstEntry = _entries.Count;
        AddPageEntries(title, url, markdown, codeAnnotations);
        if (tags is not { Count: > 0 }) return;
        var pageTags = tags.ToArray();
        for (var index = firstEntry; index < _entries.Count; index++)
            _entries[index] = _entries[index] with { Tags = pageTags };
    }

    /// <summary>Adds the entry of a tag page, which the search lists before the pages when the query names the tag.</summary>
    public void AddTag(string name, string url, string description, IReadOnlyList<string> aliases, int pageCount)
    {
        _entries.RemoveAll(entry => entry.PageUrl.Equals(url, StringComparison.Ordinal));
        _entries.Add(new SearchIndexEntry(name, string.Empty, url, url, WhitespaceRegex.Replace(description, " ").Trim(), string.Join(" ", aliases))
        {
            Kind = "tag",
            Count = pageCount
        });
    }

    private void AddPageEntries(string title, string url, string markdown, bool codeAnnotations)
    {
        var firstEntry = _entries.Count;
        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        // Headings in annotations have no anchor on the page, so they start no search section.
        Markdown.Renderer.CodeAnnotations.ClearHeadingIds(document, codeAnnotations);
        var parts = document.Descendants().All(IsSupportedNode)
            ? ReadMarkdown(document, codeAnnotations)
            : ReadRenderedHtml(document, _pipeline, codeAnnotations);
        var pairedMarkers = GetPairedMarkers(parts);
        var inOpenApi = pairedMarkers.Count == 0 && parts.Any(part => part.IsOperation);
        var currentEntry = -1;
        var outsideEntry = -1;
        var operationEntry = -1;
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (pairedMarkers.Contains(index))
            {
                inOpenApi = part.Marker == SearchMarker.OpenApiStart;
                if (inOpenApi) outsideEntry = currentEntry;
                else currentEntry = outsideEntry;
                operationEntry = -1;
                continue;
            }
            if (part.Marker != SearchMarker.None) continue;

            var text = NormalizeText(part.Text);
            if (part.Title is null || string.IsNullOrEmpty(part.Id))
            {
                if (part.IsApiSection) operationEntry = -1;
                var content = part.Title is null ? text : $"{part.Title} {text}".Trim();
                if (currentEntry >= 0) AppendToEntry(currentEntry, content, part.Code);
                else if (content.Length > 0 || part.Code.Count > 0)
                {
                    AddEntry(title, string.Empty, url, content, part.Code);
                    currentEntry = _entries.Count - 1;
                    if (outsideEntry < 0) outsideEntry = currentEntry;
                }
                continue;
            }

            if (inOpenApi && operationEntry >= 0 && !part.IsApiSection)
            {
                AppendToEntry(operationEntry, $"{part.Title} {text}".Trim(), part.Code);
                currentEntry = operationEntry;
                continue;
            }
            AddEntry(title, part.Title, $"{url}#{Uri.EscapeDataString(part.Id)}", text, part.Code);
            currentEntry = _entries.Count - 1;
            operationEntry = inOpenApi && part.IsOperation ? currentEntry : -1;
        }
        if (_entries.Count == firstEntry) AddEntry(title, string.Empty, url, string.Empty, null);
    }

    private static HashSet<int> GetPairedMarkers(IReadOnlyList<SearchPart> parts)
    {
        var paired = new HashSet<int>();
        var start = -1;
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].Marker == SearchMarker.OpenApiStart && start < 0) start = index;
            else if (parts[index].Marker == SearchMarker.OpenApiEnd && start >= 0)
            {
                paired.Add(start);
                paired.Add(index);
                start = -1;
            }
        }
        return paired;
    }

    private static readonly HashSet<Type> SupportedNodes =
    [
        typeof(HeadingBlock), typeof(ParagraphBlock), typeof(ListBlock), typeof(ListItemBlock), typeof(QuoteBlock),
        typeof(ThematicBreakBlock), typeof(LinkReferenceDefinitionGroup), typeof(LinkReferenceDefinition),
        typeof(ContainerInline), typeof(LiteralInline), typeof(EmphasisInline), typeof(LinkInline), typeof(CodeInline),
        typeof(HtmlEntityInline), typeof(LineBreakInline), typeof(AutolinkInline), typeof(Table), typeof(TableRow),
        typeof(Markdig.Extensions.Tables.TableCell), typeof(AlertBlock), typeof(KeysInline), typeof(EmojiShortcodeInline), typeof(TaskList),
        typeof(IconInline), typeof(MathInline), typeof(FootnoteLink), typeof(FootnoteGroup), typeof(Footnote),
        typeof(FootnoteLinkReferenceDefinition), typeof(HeadingLinkReferenceDefinition), typeof(ResultBlock)
    ];

    private static bool IsSupportedNode(MarkdownObject node) => node is CodeBlock ||
        SupportedNodes.Contains(node.GetType()) &&
        (node is not AlertBlock alert || GitHubAlertExtension.GetTitle(alert.Kind.ToString()) is not null);

    internal enum SearchMarker { None, OpenApiStart, OpenApiEnd }

    private static SearchMarker ReadOpenApiMarker(ReadOnlySpan<char> content) =>
        content.SequenceEqual(GetMarkerContent(OpenApiMarkdownRenderer.BlockStartMarker))
            ? SearchMarker.OpenApiStart
            : content.SequenceEqual(GetMarkerContent(OpenApiMarkdownRenderer.BlockEndMarker))
                ? SearchMarker.OpenApiEnd : SearchMarker.None;

    private static ReadOnlySpan<char> GetMarkerContent(string marker) => marker.AsSpan(4..^3);

    private static string NormalizeText(StringBuilder text) => WhitespaceRegex.Replace(text.ToString(), " ").Trim();

    // These elements cannot contribute visible content on the rendered, JavaScript-enabled site.
    private static bool IsExcludedElement(string name) => name is
        "script" or "style" or "template" or "iframe" or "noembed" or "noframes" or "noscript";

    internal sealed class SearchPart(string? id = null, string? title = null, bool isOperation = false, bool isApiSection = false, SearchMarker marker = SearchMarker.None)
    {
        public string? Id { get; } = id;
        public string? Title { get; set; } = title;
        public bool IsOperation { get; } = isOperation;
        public bool IsApiSection { get; } = isApiSection;
        public SearchMarker Marker { get; } = marker;
        public StringBuilder Text { get; } = new();
        public List<string> Code { get; } = new();
    }

    /// <param name="document">The parsed page.</param>
    /// <param name="codeAnnotations">Whether the page's code blocks show annotations, whose markers are then left out.</param>
    internal List<SearchPart> ReadMarkdown(MarkdownDocument document, bool codeAnnotations = true)
    {
        var parts = new List<SearchPart> { new() };
        void Read(MarkdownObject node)
        {
            if (node is HeadingBlock heading && heading.GetAttributes().Id is not null)
            {
                var text = new StringBuilder();
                var attributes = heading.GetAttributes();
                var operation = attributes.Classes?.Contains("openapi-operation") == true;
                var part = new SearchPart(attributes.Id, string.Empty, operation,
                    operation || attributes.Classes?.Contains("openapi-tag") == true || attributes.Classes?.Contains("openapi-overview") == true);
                if (heading.Inline is not null) ReadInline(heading.Inline, text, part.Code);
                part.Title = NormalizeText(text);
                parts.Add(part);
            }
            else if (node is CodeBlock code)
            {
                if (IsSearchableCodeBlock(code)) AddCode(parts[^1].Code, GetCode(code, codeAnnotations));
                parts[^1].Text.Append(' ');
            }
            else if (node is ParagraphBlock or HeadingBlock)
            {
                // A heading without an id belongs to a result: there is no section to link to, so it reads as text.
                if (((LeafBlock)node).Inline is { } inline) ReadInline(inline, parts[^1].Text, parts[^1].Code);
                parts[^1].Text.Append(' ');
            }
            else if (node is ContainerBlock container)
            {
                if (node is AlertBlock alert)
                {
                    parts[^1].Text.Append(GitHubAlertExtension.GetTitle(alert.Kind.ToString())).Append(' ');
                }
                else if (node is ResultBlock)
                {
                    parts[^1].Text.Append(ResultBlock.Title).Append(' ');
                }
                foreach (var child in container) Read(child);
                parts[^1].Text.Append(' ');
            }
        }
        Read(document);
        return parts;

        void ReadInline(Inline inline, StringBuilder text, List<string> codeValues)
        {
            switch (inline)
            {
                case LiteralInline literal: text.Append(literal.Content.AsSpan()); break;
                case CodeInline code:
                    AddCode(codeValues, code.Content);
                    NormalizeMarkdownLinks(code);
                    text.Append(' ').Append(code.Content).Append(' ');
                    break;
                case HtmlEntityInline entity: text.Append(entity.Transcoded.AsSpan()); break;
                case LineBreakInline: text.Append(' '); break;
                case LinkInline { IsImage: true } image:
                    foreach (var code in image.Descendants().OfType<CodeInline>()) AddCode(codeValues, code.Content);
                    text.Append(' ');
                    break;
                case EmojiShortcodeInline emoji: text.Append(' ').Append(emoji.Emoji).Append(' '); break;
                case TaskList: text.Append(' '); break;
                case FootnoteLink footnote:
                    text.Append(' ');
                    if (footnote.IsBackLink) text.Append('↩');
                    else text.Append(footnote.Footnote.Order);
                    text.Append(' ');
                    break;
                case MathInline math:
                    text.Append(" \\(").Append(math.Content.AsSpan()).Append("\\) ");
                    break;
                case IconInline icon:
                    AppendIconText(icon.Icon.Glyph, text);
                    if (icon.Icon.DarkGlyph is { } dark) AppendIconText(dark, text);
                    break;
                case KeysInline keys:
                    text.Append(' ');
                    for (var index = 0; index < keys.Keys.Count; index++)
                    {
                        if (index > 0) text.Append(" + ");
                        var key = keys.Keys[index];
                        if (key.Key?.Symbol is { } symbol) text.Append(symbol).Append(' ');
                        text.Append(key.Label).Append(' ');
                    }
                    break;
                case AutolinkInline link: text.Append(' ').Append(link.Url).Append(' '); break;
                case ContainerInline container:
                    if (inline.GetType() != typeof(ContainerInline)) text.Append(' ');
                    foreach (var child in container) ReadInline(child, text, codeValues);
                    if (inline.GetType() != typeof(ContainerInline)) text.Append(' ');
                    break;
            }
        }

        // Simple SVG uses tokens; unusual fragments retain their SVG parsing context.
        void AppendIconText(IconGlyph glyph, StringBuilder text)
        {
            if (!_iconText.TryGetValue(glyph, out var content))
            {
                var glyphText = new StringBuilder();
                if (TryReadTokens(glyph.Body, svgContext: true, out var glyphParts) && glyphParts.Count == 1)
                    glyphText.Append(glyphParts[0].Text);
                else
                {
                    _svgContext ??= HtmlParser.ParseDocument(string.Empty).CreateElement("http://www.w3.org/2000/svg", "svg");
                    foreach (var node in HtmlParser.ParseFragment(glyph.Body, _svgContext)) AppendHtmlText(node, glyphText);
                }
                content = glyphText.ToString();
                _iconText.Add(glyph, content);
            }
            text.Append(' ').Append(content).Append(' ');
        }
    }

    internal List<SearchPart> ReadRenderedHtml(MarkdownDocument document, MarkdownPipeline pipeline, bool codeAnnotations = true)
    {
        var prefix = _codePrefix;
        var code = new List<string>();
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new SearchCodeBlockRenderer(code, prefix, codeAnnotations));
        renderer.ObjectRenderers.Insert(0, new SearchCodeInlineRenderer(code, prefix));
        renderer.ObjectRenderers.Insert(0, new SearchLinkInlineRenderer(code, prefix));
        renderer.Render(document);
        return ReadHtml(writer.ToString(), code, prefix);
    }

    private static void WriteCodeMarker(Markdig.Renderers.HtmlRenderer renderer, string code, List<string> values, string prefix)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        renderer.Write("<!--").Write(prefix).Write(values.Count.ToString(CultureInfo.InvariantCulture)).Write("-->");
        values.Add(code.Trim());
    }

    private sealed class SearchCodeBlockRenderer(List<string> code, string prefix, bool codeAnnotations) : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(Markdig.Renderers.HtmlRenderer renderer, CodeBlock block)
        {
            if (IsSearchableCodeBlock(block)) WriteCodeMarker(renderer, GetCode(block, codeAnnotations), code, prefix);
        }
    }

    private sealed class SearchCodeInlineRenderer(List<string> code, string prefix) : Markdig.Renderers.Html.Inlines.CodeInlineRenderer
    {
        protected override void Write(Markdig.Renderers.HtmlRenderer renderer, CodeInline inline)
        {
            if (renderer.EnableHtmlForInline) WriteCodeMarker(renderer, inline.Content, code, prefix);
            NormalizeMarkdownLinks(inline);
            base.Write(renderer, inline);
        }
    }

    private sealed class SearchLinkInlineRenderer(List<string> code, string prefix) : Markdig.Renderers.Html.Inlines.LinkInlineRenderer
    {
        protected override void Write(Markdig.Renderers.HtmlRenderer renderer, LinkInline inline)
        {
            if (inline.IsImage && renderer.EnableHtmlForInline)
                foreach (var value in inline.Descendants().OfType<CodeInline>()) WriteCodeMarker(renderer, value.Content, code, prefix);
            base.Write(renderer, inline);
        }
    }

    private static bool TryReadCode(ReadOnlySpan<char> comment, IReadOnlyList<string>? values, string? prefix, out string code)
    {
        code = string.Empty;
        if (values is null || prefix is null || !comment.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(comment[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            (uint)index >= (uint)values.Count) return false;
        code = values[index];
        return true;
    }

    // In raw-text elements a generated code comment is text rather than a DOM comment.
    private static void AppendText(ReadOnlySpan<char> value, StringBuilder text, List<string> target, IReadOnlyList<string>? code, string? prefix)
    {
        while (code is { Count: > 0 } && prefix is not null)
        {
            var start = value.IndexOf("<!--", StringComparison.Ordinal);
            if (start < 0) break;
            var end = value[(start + 4)..].IndexOf("-->", StringComparison.Ordinal);
            if (end < 0) break;
            text.Append(value[..start]);
            var length = end + 7;
            if (TryReadCode(value.Slice(start + 4, end), code, prefix, out var item))
            {
                target.Add(item);
                text.Append(' ');
            }
            else text.Append(value.Slice(start, length));
            value = value[(start + length)..];
        }
        text.Append(value);
    }

    // This is a conservative fast path, not an HTML tree builder. Ambiguous structures use the DOM path.
    internal static bool TryReadTokens(string html, bool svgContext, out List<SearchPart> parts, IReadOnlyList<string>? codeValues = null, string? codePrefix = null)
    {
        parts = new List<SearchPart> { new() };
        using var source = new TextSource(html);
        using var tokenizer = new HtmlTokenizer(source, HtmlEntityProvider.ResolverExtended);
        var error = false;
        tokenizer.Error += (_, _) => error = true;
        var stack = new List<string>();
        var contextDepth = svgContext ? 1 : 0;
        if (svgContext) stack.Add("svg");
        var svgDepth = contextDepth;
        var hiddenDepth = 0;
        StringBuilder? heading = null;
        List<string>? headingCode = null;
        string? headingId = null;
        var operation = false;
        var apiSection = false;
        while (true)
        {
            tokenizer.IsAcceptingCharacterData = svgDepth > 0;
            ref var token = ref tokenizer.GetStructToken();
            if (error) return false;
            switch (token.Type)
            {
                case HtmlTokenType.EndOfFile: return stack.Count == contextDepth && heading is null;
                case HtmlTokenType.Character:
                    if (hiddenDepth > 0) break;
                    var text = token.Data.Memory.Span;
                    if (stack.Count > 0 && stack[^1] is "table" or "thead" or "tbody" or "tfoot" or "tr" or "ul" or "ol")
                        if (!text.IsWhiteSpace()) return false;
                    AppendText(text, heading ?? parts[^1].Text, headingCode ?? parts[^1].Code, codeValues, codePrefix);
                    break;
                case HtmlTokenType.Comment:
                    if (hiddenDepth > 0) break;
                    (heading ?? parts[^1].Text).Append(' ');
                    if (TryReadCode(token.Data.Memory.Span, codeValues, codePrefix, out var code))
                        (headingCode ?? parts[^1].Code).Add(code);
                    else
                    {
                        var marker = ReadOpenApiMarker(token.Data.Memory.Span);
                        if (marker == SearchMarker.None) break;
                        if (heading is not null) return false;
                        parts.Add(new SearchPart(marker: marker));
                        parts.Add(new SearchPart());
                    }
                    break;
                case HtmlTokenType.StartTag:
                    var name = CanonicalTag(token.Name.Memory.Span);
                    if (name is null || !CanInsertTag(stack, name, svgDepth > 0, heading is not null && hiddenDepth == 0)) return false;
                    var inSvg = svgDepth > 0 || name == "svg";
                    var isVoid = !inSvg && name is "br" or "hr" or "img" or "input" or "source" or "track";
                    if (token.IsSelfClosing && !inSvg && !isVoid) return false;
                    var hidden = IsExcludedElement(name);
                    if (!hidden && hiddenDepth == 0 && IsHeading(name))
                    {
                        heading = new StringBuilder();
                        headingCode = new List<string>();
                        var id = token.GetAttribute("id");
                        headingId = id.Memory.Length == 0 ? null : id.ToString();
                        var classes = token.GetAttribute("class").Memory.Span;
                        operation = HasHtmlClass(classes, "openapi-operation");
                        apiSection = operation || HasHtmlClass(classes, "openapi-tag") || HasHtmlClass(classes, "openapi-overview");
                    }
                    else if (hiddenDepth == 0) (heading ?? parts[^1].Text).Append(' ');
                    if (!isVoid && !token.IsSelfClosing)
                    {
                        stack.Add(name);
                        if (name == "svg") svgDepth++;
                        if (hidden) hiddenDepth++;
                        if (!inSvg)
                        {
                            if (name == "script") tokenizer.State = HtmlParseMode.Script;
                            else if (name is "style" or "iframe" or "noembed" or "noframes" or "noscript") tokenizer.State = HtmlParseMode.Rawtext;
                            else if (name == "textarea") tokenizer.State = HtmlParseMode.RCData;
                        }
                    }
                    break;
                case HtmlTokenType.EndTag:
                    var endName = CanonicalTag(token.Name.Memory.Span);
                    if (endName is null || stack.Count <= contextDepth || stack[^1] != endName) return false;
                    stack.RemoveAt(stack.Count - 1);
                    if (endName == "svg") svgDepth--;
                    if (IsExcludedElement(endName))
                    {
                        hiddenDepth--;
                        if (hiddenDepth == 0) (heading ?? parts[^1].Text).Append(' ');
                        break;
                    }
                    if (hiddenDepth > 0) break;
                    if (IsHeading(endName))
                    {
                        var part = new SearchPart(headingId, NormalizeText(heading!), operation, apiSection);
                        part.Code.AddRange(headingCode!);
                        parts.Add(part);
                        heading = null;
                        headingCode = null;
                    }
                    else (heading ?? parts[^1].Text).Append(' ');
                    break;
                default: return false;
            }
        }
    }

    private static bool HasHtmlClass(ReadOnlySpan<char> value, string name)
    {
        foreach (var range in value.SplitAny(" \t\r\n\f"))
            if (value[range].SequenceEqual(name)) return true;
        return false;
    }

    private static bool IsHeading(string name) => name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6';

    private static bool CanInsertTag(List<string> stack, string name, bool inSvg, bool inHeading)
    {
        var svgTag = name is "svg" or "path" or "g" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text" or "tspan" or "title" or "desc" or "defs" or "clippath" or "mask" or "filter" or "lineargradient" or "radialgradient" or "stop" or "pattern" or "use" or "symbol" or "marker" or "switch" or "image" or "textpath" or "a" or "animate" or "animatemotion" or "animatetransform" or "set" or "style";
        // SVG title/desc can integrate HTML children; styles support only text on this path.
        if (inSvg) return stack[^1] is not ("title" or "desc" or "style") && svgTag;
        if (svgTag && name is not ("svg" or "a" or "script" or "style")) return false;
        var phrasing = name is "a" or "abbr" or "b" or "br" or "code" or "del" or "em" or "i" or "img" or "input" or "ins" or "kbd" or "mark" or "s" or "small" or "span" or "strong" or "sub" or "sup" or "u" or "svg" or "script" or "style" or "template" or "textarea" or "video" or "audio" or "source" or "track";
        if (!phrasing && (inHeading || stack.Contains("p"))) return false;
        if (name == "a" && stack.Contains("a")) return false;
        var parent = stack.Count == 0 ? string.Empty : stack[^1];
        if (parent is "table" or "thead" or "tbody" or "tfoot" or "tr" or "ul" or "ol")
        {
            var allowed = parent switch
            {
                "table" => name is "thead" or "tbody" or "tfoot",
                "thead" or "tbody" or "tfoot" => name == "tr",
                "tr" => name is "td" or "th",
                _ => name == "li"
            };
            if (!allowed) return false;
        }
        return name switch
        {
            "thead" or "tbody" or "tfoot" => parent == "table",
            "tr" => parent is "thead" or "tbody" or "tfoot",
            "td" or "th" => parent == "tr",
            "li" => parent is "ul" or "ol",
            "summary" => parent == "details",
            "source" or "track" => parent is "video" or "audio",
            _ => true
        };
    }

    private static string? CanonicalTag(ReadOnlySpan<char> name) => name switch
    {
        "a" => "a", "abbr" => "abbr", "b" => "b", "blockquote" => "blockquote", "br" => "br", "code" => "code",
        "del" => "del", "details" => "details", "div" => "div", "em" => "em", "h1" => "h1", "h2" => "h2",
        "h3" => "h3", "h4" => "h4", "h5" => "h5", "h6" => "h6", "hr" => "hr", "i" => "i", "img" => "img",
        "input" => "input", "ins" => "ins", "kbd" => "kbd", "li" => "li", "mark" => "mark", "ol" => "ol",
        "p" => "p", "pre" => "pre", "s" => "s", "section" => "section", "small" => "small", "span" => "span",
        "strong" => "strong", "sub" => "sub", "summary" => "summary", "sup" => "sup", "table" => "table",
        "tbody" => "tbody", "td" => "td", "tfoot" => "tfoot", "th" => "th", "thead" => "thead", "tr" => "tr", "u" => "u", "ul" => "ul",
        "svg" => "svg", "path" => "path", "g" => "g", "rect" => "rect", "circle" => "circle", "ellipse" => "ellipse",
        "line" => "line", "polyline" => "polyline", "polygon" => "polygon", "text" => "text", "tspan" => "tspan", "title" => "title", "desc" => "desc",
        "video" => "video", "audio" => "audio", "source" => "source", "track" => "track", "script" => "script", "style" => "style", "template" => "template", "textarea" => "textarea",
        "defs" => "defs", "clippath" => "clippath", "mask" => "mask", "filter" => "filter", "lineargradient" => "lineargradient", "radialgradient" => "radialgradient", "stop" => "stop", "pattern" => "pattern", "use" => "use", "symbol" => "symbol", "marker" => "marker", "switch" => "switch", "image" => "image", "textpath" => "textpath", "animate" => "animate", "animatemotion" => "animatemotion", "animatetransform" => "animatetransform", "set" => "set",
        _ => null
    };

    internal List<SearchPart> ReadHtml(string html, IReadOnlyList<string>? code = null, string? prefix = null)
    {
        if (TryReadTokens(html, svgContext: false, out var parts, code, prefix)) return parts;
        return ReadHtmlDom(html, code, prefix, HtmlParser);
    }

    internal static List<SearchPart> ReadHtmlDom(string html, IReadOnlyList<string>? code = null, string? prefix = null, HtmlParser? parser = null)
    {
        parser ??= new HtmlParser(new HtmlParserOptions { IsScripting = true });
        using var document = parser.ParseDocument(html);
        var parts = new List<SearchPart> { new() };
        void Read(INode node)
        {
            if (node is IComment comment)
            {
                parts[^1].Text.Append(' ');
                if (TryReadCode(comment.Data.AsSpan(), code, prefix, out var value)) parts[^1].Code.Add(value);
                else
                {
                    var marker = ReadOpenApiMarker(comment.Data.AsSpan());
                    if (marker == SearchMarker.None) return;
                    parts.Add(new SearchPart(marker: marker));
                    parts.Add(new SearchPart());
                }
            }
            else if (node is IText text) AppendText(text.Data.AsSpan(), parts[^1].Text, parts[^1].Code, code, prefix);
            else if (node is IElement element)
            {
                if (IsExcludedElement(element.LocalName))
                {
                    parts[^1].Text.Append(' ');
                    return;
                }
                if (IsHeading(element.LocalName))
                {
                    var title = new StringBuilder();
                    var headingCode = new List<string>();
                    AppendHtmlText(element, title, headingCode, code, prefix);
                    var operation = element.ClassList.Contains("openapi-operation");
                    var part = new SearchPart(element.GetAttribute("id"), NormalizeText(title), operation,
                        operation || element.ClassList.Contains("openapi-tag") || element.ClassList.Contains("openapi-overview"));
                    part.Code.AddRange(headingCode);
                    parts.Add(part);
                    return;
                }
                parts[^1].Text.Append(' ');
                foreach (var child in node.ChildNodes) Read(child);
                parts[^1].Text.Append(' ');
            }
            else foreach (var child in node.ChildNodes) Read(child);
        }
        Read(document);
        return parts;
    }

    private static void AppendHtmlText(INode node, StringBuilder text, List<string>? target = null, IReadOnlyList<string>? code = null, string? prefix = null)
    {
        if (node is IText content)
        {
            if (target is null) text.Append(content.Data);
            else AppendText(content.Data.AsSpan(), text, target, code, prefix);
        }
        else if (node is IComment comment)
        {
            text.Append(' ');
            if (target is not null && TryReadCode(comment.Data.AsSpan(), code, prefix, out var value)) target.Add(value);
        }
        else if (node is IElement element)
        {
            text.Append(' ');
            if (IsExcludedElement(element.LocalName)) return;
            foreach (var child in node.ChildNodes) AppendHtmlText(child, text, target, code, prefix);
            text.Append(' ');
        }
    }

    private void AppendToEntry(int index, string text, List<string>? code)
    {
        var entry = _entries[index];
        var joinedText = string.IsNullOrWhiteSpace(text) ? entry.Text : $"{entry.Text} {text}".Trim();
        var joinedCode = code is { Count: > 0 } ? $"{entry.Code} {string.Join(" ", code)}".Trim() : entry.Code;
        _entries[index] = entry with { Text = joinedText, Code = joinedCode };
    }

    public void WriteIndex(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        var indexPath = Path.Combine(outputPath, "search-index.json");
        var temporaryPath = indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(Encoding.UTF8.Preamble);
                JsonSerializer.Serialize(stream, _entries, JsonContext.ListSearchIndexEntry);
            }
            File.Move(temporaryPath, indexPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void AddEntry(string title, string section, string url, string text, List<string>? code) =>
        _entries.Add(new SearchIndexEntry(title, section, url, url.Split('#')[0], text, string.Join(" ", code ?? [])));

    private static void AddCode(List<string> code, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) code.Add(value.Trim());
    }

    // Annotated code is indexed without its markers, which readers see as buttons rather than as code. Where the page
    // turns annotations off, the markers stay, as on the page.
    private static string GetCode(CodeBlock block, bool codeAnnotations) =>
        block is FencedCodeBlock fenced
            ? Markdown.Renderer.CodeAnnotations.SearchText(fenced, codeAnnotations).ReplaceLineEndings(Environment.NewLine) :
        block is LeafBlock leaf && leaf.Lines.Lines is not null
            ? string.Join(Environment.NewLine, leaf.Lines.Lines.Select(line => line.Slice.ToString()))
            : string.Empty;

    private static bool IsSearchableCodeBlock(CodeBlock block)
    {
        if (block is not FencedCodeBlock fenced) return true;
        var info = fenced.Info?.Trim() ?? string.Empty;
        var languageEnd = info.IndexOfAny([' ', '\t']);
        var language = languageEnd < 0 ? info : info[..languageEnd];

        // Diagrams, formulas, and page components are not code; openapi and tags blocks expand into the page before it is indexed.
        return Markdown.Renderer.HighlightedCodeBlockRenderer.IsCodeLanguage(language) &&
               !language.Equals(Markdown.Renderer.OpenApiMarkdownRenderer.CodeBlockLanguage, StringComparison.OrdinalIgnoreCase) &&
               !language.Equals("tags", StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeMarkdownLinks(CodeInline code)
    {
        if (!code.Content.Contains("](", StringComparison.Ordinal)) return;
        code.Content = InlineMarkdownLinkRegex.Replace(code.Content, match =>
            Markdig.Markdown.ToPlainText(match.Groups["label"].Value, LinkLabelPipeline).Trim());
    }

    private static readonly MarkdownPipeline LinkLabelPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
}

internal sealed record SearchIndexEntry(
    string Title,
    string Section,
    string Url,
    [property: JsonIgnore] string PageUrl,
    string Text,
    string Code)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? Tags { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Count { get; init; }
}

[JsonSerializable(typeof(List<SearchIndexEntry>))]
internal partial class SearchIndexJsonContext : JsonSerializerContext
{
}
