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
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseGridTables()
        .UsePipeTables()
        .UseAdvancedExtensions()
        .UseHeadingIds()
        .UseGitHubAlerts()
        .UseIconsAndEmojis()
        .UseKeyboardKeys()
        .Build();

    public void Clear() => _entries.Clear();

    public void AddPage(string title, string url, string markdown)
    {
        _entries.RemoveAll(entry => entry.PageUrl.Equals(url, StringComparison.Ordinal));

        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        var codeBySection = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sectionId = string.Empty;
        var sectionIdBeforeOpenApiBlock = string.Empty;
        var canReadMarkdown = true;
        var codeBlocks = new List<CodeBlock>();
        foreach (var item in document.Descendants())
        {
            if (item is HeadingBlock heading)
                sectionId = heading.GetAttributes().Id ?? string.Empty;
            else if (item is HtmlBlock htmlBlock)
            {
                // Code after an API reference belongs to the section that contains the reference, not to its last operation.
                var blockHtml = htmlBlock.Lines.ToString();
                if (blockHtml.Contains(OpenApiMarkdownRenderer.BlockStartMarker, StringComparison.Ordinal))
                    sectionIdBeforeOpenApiBlock = sectionId;
                if (blockHtml.Contains(OpenApiMarkdownRenderer.BlockEndMarker, StringComparison.Ordinal))
                    sectionId = sectionIdBeforeOpenApiBlock;
            }

            if (item is CodeInline inline)
            {
                AddCode(codeBySection, sectionId, inline.Content);
                NormalizeMarkdownLinks(inline);
            }
            else if (item is CodeBlock block)
            {
                if (IsSearchableCodeBlock(block))
                    AddCode(codeBySection, sectionId, GetCode(block));
                codeBlocks.Add(block);
            }
            if (item is not CodeBlock && canReadMarkdown && !IsSupportedNode(item)) canReadMarkdown = false;
        }
        foreach (var block in codeBlocks) block.Parent!.Remove(block);

        var parts = canReadMarkdown ? ReadMarkdown(document) : ReadHtml(document.ToHtml(_pipeline));
        var hasMarkers = parts.Any(part => part.Marker != 0);
        var inOpenApi = !hasMarkers && parts.Any(part => part.IsOperation);
        var outsideEntry = -1;
        var operationEntry = -1;
        foreach (var part in parts)
        {
            if (part.Marker != 0)
            {
                inOpenApi = part.Marker == 1;
                operationEntry = -1;
                continue;
            }

            var text = WhitespaceRegex.Replace(part.Text.ToString(), " ").Trim();
            var code = part.Title is not null && string.IsNullOrEmpty(part.Id)
                ? null
                : codeBySection.GetValueOrDefault(part.Id ?? string.Empty);
            if (part.Title is null)
            {
                if (outsideEntry >= 0) AppendToEntry(outsideEntry, text, null);
                else if (text.Length > 0 || code is { Count: > 0 } || parts.Count == 1)
                {
                    AddEntry(title, string.Empty, url, text, code);
                    outsideEntry = _entries.Count - 1;
                }
                continue;
            }

            if (inOpenApi && operationEntry >= 0 && !part.IsApiSection)
            {
                AppendToEntry(operationEntry, $"{part.Title} {text}".Trim(), code);
                continue;
            }
            operationEntry = -1;
            if (string.IsNullOrEmpty(part.Id)) continue;
            AddEntry(title, part.Title, $"{url}#{Uri.EscapeDataString(part.Id)}", text, code);
            if (inOpenApi && part.IsOperation) operationEntry = _entries.Count - 1;
            else if (!inOpenApi) outsideEntry = _entries.Count - 1;
        }
    }

    // Unknown extension nodes use the HTML path until their visible text semantics are covered explicitly.
    private static bool IsSupportedNode(MarkdownObject node) =>
        node.GetType() == typeof(HeadingBlock) || node.GetType() == typeof(ParagraphBlock) ||
        node.GetType() == typeof(ListBlock) || node.GetType() == typeof(ListItemBlock) ||
        node.GetType() == typeof(QuoteBlock) || node.GetType() == typeof(ThematicBreakBlock) ||
        node.GetType() == typeof(LinkReferenceDefinitionGroup) || node.GetType() == typeof(LinkReferenceDefinition) ||
        node.GetType() == typeof(ContainerInline) || node.GetType() == typeof(LiteralInline) ||
        node.GetType() == typeof(EmphasisInline) || node.GetType() == typeof(LinkInline) ||
        node.GetType() == typeof(CodeInline) || node.GetType() == typeof(HtmlEntityInline) ||
        node.GetType() == typeof(LineBreakInline) || node.GetType() == typeof(AutolinkInline) ||
        node.GetType() == typeof(Table) || node.GetType() == typeof(TableRow) || node.GetType() == typeof(TableCell) ||
        node.GetType() == typeof(AlertBlock) && IsKnownAlert((AlertBlock)node) || node.GetType() == typeof(KeysInline) ||
        node.GetType() == typeof(EmojiShortcodeInline) || node.GetType() == typeof(TaskList) ||
        node.GetType() == typeof(IconInline) || node.GetType() == typeof(MathInline) ||
        node.GetType() == typeof(FootnoteLink) || node.GetType() == typeof(FootnoteGroup) ||
        node.GetType() == typeof(Footnote) || node.GetType() == typeof(FootnoteLinkReferenceDefinition) ||
        node.GetType() == typeof(HeadingLinkReferenceDefinition);

    private static bool IsKnownAlert(AlertBlock alert) =>
        alert.Kind.ToString().ToUpperInvariant() is "NOTE" or "TIP" or "IMPORTANT" or "WARNING" or "CAUTION";

    private sealed class SearchPart(string? id = null, string? title = null, bool isOperation = false, bool isApiSection = false, int marker = 0)
    {
        public string? Id { get; } = id;
        public string? Title { get; } = title;
        public bool IsOperation { get; } = isOperation;
        public bool IsApiSection { get; } = isApiSection;
        public int Marker { get; } = marker;
        public StringBuilder Text { get; } = new();
    }

    private static List<SearchPart> ReadMarkdown(MarkdownDocument document)
    {
        var parts = new List<SearchPart> { new() };
        HtmlParser? iconParser = null;
        IElement? iconContext = null;
        void Read(MarkdownObject node)
        {
            if (node is HeadingBlock heading)
            {
                var text = new StringBuilder();
                if (heading.Inline is not null) ReadInline(heading.Inline, text);
                var attributes = heading.GetAttributes();
                var operation = attributes.Classes?.Contains("openapi-operation") == true;
                parts.Add(new SearchPart(attributes.Id, WhitespaceRegex.Replace(text.ToString(), " ").Trim(), operation,
                    operation || attributes.Classes?.Contains("openapi-tag") == true || attributes.Classes?.Contains("openapi-overview") == true));
            }
            else if (node is FootnoteGroup footnotes)
            {
                foreach (var footnote in footnotes.OfType<Footnote>().Where(note => note.Order > 0).OrderBy(note => note.Order)) Read(footnote);
            }
            else if (node is ParagraphBlock paragraph)
            {
                if (paragraph.Inline is not null) ReadInline(paragraph.Inline, parts[^1].Text);
                parts[^1].Text.Append(' ');
            }
            else if (node is ContainerBlock container)
            {
                if (node is AlertBlock alert)
                {
                    var kind = alert.Kind.ToString();
                    parts[^1].Text.Append(char.ToUpperInvariant(kind[0])).Append(kind.AsSpan(1).ToString().ToLowerInvariant()).Append(' ');
                }
                foreach (var child in container) Read(child);
                parts[^1].Text.Append(' ');
            }
        }
        Read(document);
        return parts;

        void ReadInline(Inline inline, StringBuilder text)
        {
            switch (inline)
            {
                case LiteralInline literal: text.Append(literal.Content.AsSpan()); break;
                case CodeInline code: text.Append(' ').Append(code.Content).Append(' '); break;
                case HtmlEntityInline entity: text.Append(entity.Transcoded.AsSpan()); break;
                case LineBreakInline: text.Append(' '); break;
                case LinkInline { IsImage: true }: text.Append(' '); break;
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
                    AppendIconText(icon.Icon.Glyph.Body, text);
                    if (icon.Icon.DarkGlyph is { } dark) AppendIconText(dark.Body, text);
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
                    foreach (var child in container) ReadInline(child, text);
                    if (inline.GetType() != typeof(ContainerInline)) text.Append(' ');
                    break;
            }
        }

        // Simple SVG uses tokens; unusual fragments retain their SVG parsing context.
        void AppendIconText(string body, StringBuilder text)
        {
            if (TryReadTokens(body, svgContext: true, out var parts) && parts.Count == 1)
            {
                text.Append(' ').Append(parts[0].Text).Append(' ');
                return;
            }
            parts.Clear();
            iconParser ??= new HtmlParser();
            iconContext ??= iconParser.ParseDocument(string.Empty).CreateElement("http://www.w3.org/2000/svg", "svg");
            text.Append(' ');
            foreach (var node in iconParser.ParseFragment(body, iconContext)) AppendHtmlText(node, text);
            text.Append(' ');
        }
    }

    // This is a conservative fast path, not an HTML tree builder. Ambiguous structures use the DOM path.
    private static bool TryReadHtmlTokens(string html, out List<SearchPart> parts) =>
        TryReadTokens(html, svgContext: false, out parts);

    private static bool TryReadTokens(string html, bool svgContext, out List<SearchPart> parts)
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
                    (heading ?? parts[^1].Text).Append(text);
                    break;
                case HtmlTokenType.Comment:
                    if (hiddenDepth > 0) break;
                    var marker = token.Data.Memory.Span;
                    if (marker.SequenceEqual(OpenApiMarkdownRenderer.BlockStartMarker.AsSpan(4, OpenApiMarkdownRenderer.BlockStartMarker.Length - 7)) || marker.SequenceEqual(OpenApiMarkdownRenderer.BlockEndMarker.AsSpan(4, OpenApiMarkdownRenderer.BlockEndMarker.Length - 7)))
                    {
                        if (heading is not null) return false;
                        parts.Add(new SearchPart(marker: marker.SequenceEqual(OpenApiMarkdownRenderer.BlockStartMarker.AsSpan(4, OpenApiMarkdownRenderer.BlockStartMarker.Length - 7)) ? 1 : -1));
                        parts.Add(new SearchPart());
                    }
                    break;
                case HtmlTokenType.StartTag:
                    var name = CanonicalTag(token.Name.Memory.Span);
                    if (name is null || !CanInsertTag(stack, name, svgDepth > 0, heading is not null && hiddenDepth == 0)) return false;
                    var inSvg = svgDepth > 0 || name == "svg";
                    var isVoid = !inSvg && name is "br" or "hr" or "img" or "input" or "source" or "track";
                    if (token.IsSelfClosing && !inSvg && !isVoid) return false;
                    var hidden = name is "script" or "style" or "template";
                    if (!hidden && hiddenDepth == 0 && IsHeading(name))
                    {
                        heading = new StringBuilder();
                        var id = token.GetAttribute("id");
                        headingId = id.Memory.Length == 0 ? null : id.ToString();
                        var classes = token.GetAttribute("class").Memory.Span;
                        operation = HasHtmlClass(classes, "openapi-operation");
                        apiSection = operation || HasHtmlClass(classes, "openapi-tag") || HasHtmlClass(classes, "openapi-overview");
                    }
                    else if (!hidden && hiddenDepth == 0) (heading ?? parts[^1].Text).Append(' ');
                    if (!isVoid && !token.IsSelfClosing)
                    {
                        stack.Add(name);
                        if (name == "svg") svgDepth++;
                        if (hidden) hiddenDepth++;
                        if (!inSvg)
                        {
                            if (name == "script") tokenizer.State = HtmlParseMode.Script;
                            else if (name == "style") tokenizer.State = HtmlParseMode.Rawtext;
                            else if (name == "textarea") tokenizer.State = HtmlParseMode.RCData;
                        }
                    }
                    break;
                case HtmlTokenType.EndTag:
                    var endName = CanonicalTag(token.Name.Memory.Span);
                    if (endName is null || stack.Count <= contextDepth || stack[^1] != endName) return false;
                    stack.RemoveAt(stack.Count - 1);
                    if (endName == "svg") svgDepth--;
                    if (endName is "script" or "style" or "template")
                    {
                        hiddenDepth--;
                        break;
                    }
                    if (hiddenDepth > 0) break;
                    if (IsHeading(endName))
                    {
                        parts.Add(new SearchPart(headingId, WhitespaceRegex.Replace(heading!.ToString(), " ").Trim(), operation, apiSection));
                        heading = null;
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

    private static List<SearchPart> ReadHtml(string html)
    {
        if (TryReadHtmlTokens(html, out var parts)) return parts;
        parts.Clear();
        return ReadHtmlDom(html);
    }

    private static List<SearchPart> ReadHtmlDom(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var parts = new List<SearchPart> { new() };
        void Read(INode node)
        {
            if (node is IComment comment)
            {
                var marker = $"<!--{comment.Data}-->";
                if (marker == OpenApiMarkdownRenderer.BlockStartMarker || marker == OpenApiMarkdownRenderer.BlockEndMarker)
                {
                    parts.Add(new SearchPart(marker: marker == OpenApiMarkdownRenderer.BlockStartMarker ? 1 : -1));
                    parts.Add(new SearchPart());
                }
            }
            else if (node is IText text) parts[^1].Text.Append(text.Data);
            else if (node is IElement element)
            {
                if (element.LocalName is "script" or "style" or "template") return;
                if (element.LocalName.Length == 2 && element.LocalName[0] == 'h' && element.LocalName[1] is >= '1' and <= '6')
                {
                    var title = new StringBuilder();
                    AppendHtmlText(element, title);
                    var operation = element.ClassList.Contains("openapi-operation");
                    parts.Add(new SearchPart(element.GetAttribute("id"), WhitespaceRegex.Replace(title.ToString(), " ").Trim(),
                        operation, operation || element.ClassList.Contains("openapi-tag") || element.ClassList.Contains("openapi-overview")));
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

    private static void AppendHtmlText(INode node, StringBuilder text)
    {
        if (node is IText content) text.Append(content.Data);
        else if (node is IElement element && element.LocalName is not ("script" or "style" or "template"))
        {
            text.Append(' ');
            foreach (var child in node.ChildNodes) AppendHtmlText(child, text);
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
        File.WriteAllText(indexPath, JsonSerializer.Serialize(_entries, JsonContext.ListSearchIndexEntry), Encoding.UTF8);
    }

    private void AddEntry(string title, string section, string url, string text, List<string>? code) =>
        _entries.Add(new SearchIndexEntry(title, section, url, url.Split('#')[0], text, string.Join(" ", code ?? [])));

    private static void AddCode(Dictionary<string, List<string>> codeBySection, string sectionId, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!codeBySection.TryGetValue(sectionId, out var sectionCode))
            codeBySection.Add(sectionId, sectionCode = []);
        sectionCode.Add(code.Trim());
    }

    private static string GetCode(CodeBlock block) =>
        block is LeafBlock leaf && leaf.Lines.Lines is not null
            ? string.Join(Environment.NewLine, leaf.Lines.Lines.Select(line => line.Slice.ToString()))
            : string.Empty;

    private static bool IsSearchableCodeBlock(CodeBlock block)
    {
        if (block is not FencedCodeBlock fenced) return true;
        var info = fenced.Info?.Trim() ?? string.Empty;
        var languageEnd = info.IndexOfAny([' ', '\t']);
        var language = languageEnd < 0 ? info : info[..languageEnd];

        return language.ToLowerInvariant() is not (
            "mermaid" or "plantuml" or "puml" or "context" or "isometric" or "hero" or "icon-search" or "openapi");
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
    string Code);

[JsonSerializable(typeof(List<SearchIndexEntry>))]
internal partial class SearchIndexJsonContext : JsonSerializerContext
{
}
