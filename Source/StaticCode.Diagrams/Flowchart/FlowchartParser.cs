using System.Text.RegularExpressions;
using StaticCode.Diagrams.Text;

namespace StaticCode.Diagrams.Flowchart;

/// <summary>
/// Parses the flowchart subset of Mermaid syntax: nodes with basic shapes, links with labels,
/// link chains, and <c>&amp;</c> groups. Styling statements are ignored because colors come from the page theme.
/// </summary>
public sealed partial class FlowchartParser
{
    private static readonly Regex HeaderRegex = HeaderRegexPattern();

    [GeneratedRegex(
        @"^(?:flowchart|graph)(?:[ \t]+(?<direction>TB|TD|BT|LR|RL))?[ \t]*(?:;(?<rest>.*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegexPattern();
    private static readonly Regex KeywordRegex = KeywordRegexPattern();

    [GeneratedRegex(@"\G(?<keyword>[A-Za-z]+)(?=[ \t:{]|$)", RegexOptions.Compiled)]
    private static partial Regex KeywordRegexPattern();
    private static readonly Regex IdRegex = IdRegexPattern();

    [GeneratedRegex(@"\G(?:[\p{L}\p{N}_]+:)?[\p{L}\p{N}_]+", RegexOptions.Compiled)]
    private static partial Regex IdRegexPattern();
    private static readonly Regex SubgraphRegex = SubgraphRegexPattern();

    [GeneratedRegex(
        "^subgraph[ \\t]+(?<id>[^ \\t\\[]+)[ \\t]*(?:\\[(?:\\\"(?<quoted>.*?)\\\"|(?<plain>.*?))\\])?[ \\t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SubgraphRegexPattern();
    private static readonly Regex ActorRegex = ActorRegexPattern();

    [GeneratedRegex(@"^(?<id>[\p{L}\p{N}_]+)(?:[ \t]+as[ \t]+(?<label>.+))?$", RegexOptions.Compiled)]
    private static partial Regex ActorRegexPattern();
    private static readonly Regex ClickRegex = ClickRegexPattern();

    [GeneratedRegex(
        "^(?<id>[\\p{L}\\p{N}_]+)[ \\t]+(?:href[ \\t]+)?(?:\"(?<doubleUrl>[^\"]*)\"|'(?<singleUrl>[^']*)'|(?<bareUrl>[^ \\t]+))(?:[ \\t]+\"[^\"]*\")?[ \\t]*;?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ClickRegexPattern();
    private static readonly Regex ClassSuffixRegex = ClassSuffixRegexPattern();

    [GeneratedRegex(@"\G:::[\w-]+", RegexOptions.Compiled)]
    private static partial Regex ClassSuffixRegexPattern();
    private static readonly Regex TextLinkOpenRegex = TextLinkOpenRegexPattern();

    [GeneratedRegex(@"\G(?<start>[<ox])?(?<kind>--|==|-\.)(?=[ \t])", RegexOptions.Compiled)]
    private static partial Regex TextLinkOpenRegexPattern();
    private static readonly Regex SolidCloseRegex = SolidCloseRegexPattern();

    [GeneratedRegex(@"(?<line>-{3,}|--(?=[>ox]))(?<end>[>ox])?", RegexOptions.Compiled)]
    private static partial Regex SolidCloseRegexPattern();
    private static readonly Regex ThickCloseRegex = ThickCloseRegexPattern();

    [GeneratedRegex(@"(?<line>={3,}|==(?=[>ox]))(?<end>[>ox])?", RegexOptions.Compiled)]
    private static partial Regex ThickCloseRegexPattern();
    private static readonly Regex DottedCloseRegex = DottedCloseRegexPattern();

    [GeneratedRegex(@"(?<line>\.+-)(?<end>[>ox])?", RegexOptions.Compiled)]
    private static partial Regex DottedCloseRegexPattern();
    private static readonly Regex SimpleLinkRegex = SimpleLinkRegexPattern();

    [GeneratedRegex(
        @"\G(?<start>[<ox])?(?:(?<solid>-{3,}|--(?=[>ox]))|(?<thick>={3,}|==(?=[>ox]))|-(?<dots>\.+)-)(?<end>[>ox])?",
        RegexOptions.Compiled)]
    private static partial Regex SimpleLinkRegexPattern();

    private static readonly (string Open, string Close, NodeShape Shape)[] Shapes =
    {
        ("((", "))", NodeShape.Circle),
        ("([", "])", NodeShape.Stadium),
        ("[[", "]]", NodeShape.Subroutine),
        ("{{", "}}", NodeShape.Hexagon),
        ("(", ")", NodeShape.Rounded),
        ("[", "]", NodeShape.Rectangle),
        ("{", "}", NodeShape.Diamond)
    };

    private static readonly string[] UnsupportedShapeOpeners = { "(((", "[(", "[/", "[\\", ">", "@{" };
    private static readonly string[] IgnoredKeywords = { "classDef", "class", "style", "linkStyle", "accDescr" };

    private readonly string[] _lines;
    private readonly bool _context;
    private readonly Dictionary<string, FlowchartNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<FlowchartNode> _nodeOrder = new();
    private readonly List<(string Id, string Href)> _nodeLinks = new();
    private readonly List<FlowchartEdge> _edges = new();
    private readonly List<FlowchartGroup> _groups = new();
    private readonly Stack<FlowchartGroup> _groupStack = new();
    private string? _title;
    private string _text = string.Empty;
    private int _pos;
    private int _lineNumber;

    private FlowchartParser(string source, bool context)
    {
        _lines = source.ReplaceLineEndings("\n").Split('\n');
        _context = context;
    }

    /// <summary>Returns <c>true</c> when the first statement is a <c>flowchart</c> or <c>graph</c> header.</summary>
    public static bool CanParse(string source)
    {
        var firstLine = source.ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal));
        return firstLine != null && HeaderRegex.IsMatch(firstLine);
    }

    public static FlowchartDiagram Parse(string source) => new FlowchartParser(source, context: false).Parse();

    /// <summary>
    /// Parses the statements of a context diagram: flowchart nodes and links without a header, plus
    /// <c>actor Id as Label</c> for people.
    /// </summary>
    internal static FlowchartDiagram ParseContext(string source) => new FlowchartParser(source, context: true).Parse();

    private FlowchartDiagram Parse()
    {
        FlowDirection? direction = _context ? FlowDirection.TopToBottom : null;
        for (var index = 0; index < _lines.Length; index++)
        {
            var line = _lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;
            _lineNumber = index + 1;

            if (direction == null)
            {
                var header = HeaderRegex.Match(line);
                if (!header.Success) throw Error("Expected a 'flowchart' or 'graph' header.");
                direction = ToDirection(header.Groups["direction"].Value);
                if (header.Groups["rest"].Success) ParseLine(header.Groups["rest"].Value);
                continue;
            }

            ParseLine(line);
        }

        if (direction == null) throw new DiagramSyntaxException("Missing 'flowchart' header.", 1);
        foreach (var (id, href) in _nodeLinks)
            if (_nodes.TryGetValue(id, out var node)) node.Href = href;
        return new FlowchartDiagram(direction.Value, _nodeOrder, _edges, _title, _groups);
    }

    private static FlowDirection ToDirection(string value) => value switch
    {
        "BT" => FlowDirection.BottomToTop,
        "LR" => FlowDirection.LeftToRight,
        "RL" => FlowDirection.RightToLeft,
        _ => FlowDirection.TopToBottom
    };

    private void ParseLine(string line)
    {
        if (line == "end")
        {
            if (_groupStack.Count == 0) throw Error("Unexpected 'end' without a matching subgraph.");
            _groupStack.Pop();
            return;
        }
        var subgraph = SubgraphRegex.Match(line);
        if (subgraph.Success)
        {
            if (_context) throw Error("Subgraphs are only supported in Mermaid flowcharts.");
            var label = subgraph.Groups["quoted"].Success ? subgraph.Groups["quoted"].Value
                : subgraph.Groups["plain"].Success ? subgraph.Groups["plain"].Value
                : subgraph.Groups["id"].Value;
            var group = new FlowchartGroup(subgraph.Groups["id"].Value, label);
            _groups.Add(group);
            _groupStack.Push(group);
            return;
        }
        if (line.StartsWith("direction ", StringComparison.Ordinal)) return;

        _text = line;
        _pos = 0;
        while (true)
        {
            SkipSpaces();
            if (AtEnd) return;
            if (Peek == ';')
            {
                _pos++;
                continue;
            }

            if (TryHandleKeyword()) return;

            ParseStatement();
            SkipSpaces();
            if (!AtEnd && Peek != ';') throw Error($"Unexpected '{_text[_pos..]}'.");
        }
    }

    private bool TryHandleKeyword()
    {
        var match = KeywordRegex.Match(_text, _pos);
        if (!match.Success) return false;

        var keyword = match.Groups["keyword"].Value;
        if (keyword == "direction") return true;
        if (keyword == "click")
        {
            var click = ClickRegex.Match(_text[(_pos + match.Length)..].Trim());
            if (click.Success)
            {
                var href = click.Groups["doubleUrl"].Success ? click.Groups["doubleUrl"].Value
                    : click.Groups["singleUrl"].Success ? click.Groups["singleUrl"].Value
                    : click.Groups["bareUrl"].Value;
                _nodeLinks.Add((click.Groups["id"].Value, href));
            }

            return true;
        }
        if (IgnoredKeywords.Contains(keyword)) return true;

        var rest = _text[(_pos + match.Length)..].TrimStart();
        if (_context && keyword == "actor")
        {
            var actor = ActorRegex.Match(rest);
            if (!actor.Success) throw Error("Expected 'actor Id' or 'actor Id as Label'.");
            var node = GetNode(actor.Groups["id"].Value);
            node.Shape = NodeShape.Actor;
            if (actor.Groups["label"].Success) node.Label = CleanLabel(actor.Groups["label"].Value) ?? node.Id;
            return true;
        }

        if (keyword != "accTitle") return false;

        if (!rest.StartsWith(':')) throw Error("Expected ':' after 'accTitle'.");
        _title = rest[1..].Trim();
        return true;
    }

    private void ParseStatement()
    {
        var sources = ParseNodeGroup();
        while (true)
        {
            SkipSpaces();
            if (AtEnd || Peek == ';') return;

            var link = ParseLink();
            SkipSpaces();
            var targets = ParseNodeGroup();
            foreach (var source in sources)
            foreach (var target in targets)
                _edges.Add(link with { From = source.Id, To = target.Id });
            sources = targets;
        }
    }

    private List<FlowchartNode> ParseNodeGroup()
    {
        var nodes = new List<FlowchartNode> { ParseNode() };
        while (true)
        {
            var position = _pos;
            SkipSpaces();
            if (AtEnd || Peek != '&')
            {
                _pos = position;
                return nodes;
            }

            _pos++;
            SkipSpaces();
            nodes.Add(ParseNode());
        }
    }

    private FlowchartNode ParseNode()
    {
        var match = IdRegex.Match(_text, _pos);
        if (!match.Success) throw Error(AtEnd ? "Expected a node." : $"Expected a node at '{_text[_pos..]}'.");
        _pos += match.Length;
        var node = GetNode(match.Value);

        var unsupported = UnsupportedShapeOpeners.FirstOrDefault(opener => Rest.StartsWith(opener, StringComparison.Ordinal));
        if (unsupported != null) throw Error($"Node shape '{unsupported}' is not supported yet.");

        foreach (var (open, close, shape) in Shapes)
        {
            if (!Rest.StartsWith(open, StringComparison.Ordinal)) continue;
            _pos += open.Length;
            node.Label = ReadText(close);
            node.Shape = shape;
            break;
        }

        var classSuffix = ClassSuffixRegex.Match(_text, _pos);
        if (classSuffix.Success) _pos += classSuffix.Length;
        return node;
    }

    private FlowchartNode GetNode(string id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            node = new FlowchartNode(id);
            _nodes.Add(id, node);
            _nodeOrder.Add(node);
        }

        foreach (var group in _groupStack)
            if (!group.NodeIds.Contains(id, StringComparer.Ordinal)) group.NodeIds.Add(id);
        return node;
    }

    private FlowchartEdge ParseLink()
    {
        FlowchartEdge edge;
        var textLink = TextLinkOpenRegex.Match(_text, _pos);
        if (textLink.Success)
        {
            var kind = textLink.Groups["kind"].Value;
            var closeRegex = kind switch
            {
                "--" => SolidCloseRegex,
                "==" => ThickCloseRegex,
                _ => DottedCloseRegex
            };
            var textStart = textLink.Index + textLink.Length;
            var close = closeRegex.Match(_text, textStart);
            if (!close.Success) throw Error($"Missing the end of the link that starts with '{kind}'.");

            var line = close.Groups["line"].Value;
            var hasEnd = close.Groups["end"].Success;
            edge = new FlowchartEdge(
                string.Empty,
                string.Empty,
                CleanLabel(_text[textStart..close.Index]),
                kind switch { "--" => LineStyle.Solid, "==" => LineStyle.Thick, _ => LineStyle.Dotted },
                ToMarker(textLink.Groups["start"].Value),
                ToMarker(close.Groups["end"].Value),
                kind == "-." ? line.Length - 1 : LineLength(line.Length, hasEnd));
            _pos = close.Index + close.Length;
        }
        else
        {
            var link = SimpleLinkRegex.Match(_text, _pos);
            if (!link.Success) throw Error($"Expected a link such as '-->' at '{_text[_pos..]}'.");

            var hasEnd = link.Groups["end"].Success;
            var (style, length) =
                link.Groups["solid"].Success ? (LineStyle.Solid, LineLength(link.Groups["solid"].Length, hasEnd)) :
                link.Groups["thick"].Success ? (LineStyle.Thick, LineLength(link.Groups["thick"].Length, hasEnd)) :
                (LineStyle.Dotted, link.Groups["dots"].Length);
            edge = new FlowchartEdge(
                string.Empty,
                string.Empty,
                null,
                style,
                ToMarker(link.Groups["start"].Value),
                ToMarker(link.Groups["end"].Value),
                length);
            _pos += link.Length;
        }

        var position = _pos;
        SkipSpaces();
        if (!AtEnd && Peek == '|')
        {
            var end = _text.IndexOf('|', _pos + 1);
            if (end < 0) throw Error("Missing closing '|' of the link label.");
            edge = edge with { Label = CleanLabel(_text[(_pos + 1)..end]) };
            _pos = end + 1;
        }
        else
        {
            _pos = position;
        }

        return edge;
    }

    private static int LineLength(int characters, bool hasEnd) => Math.Max(1, characters - (hasEnd ? 1 : 2));

    private static EdgeMarker ToMarker(string value) => value switch
    {
        "<" or ">" => EdgeMarker.Arrow,
        "o" => EdgeMarker.Circle,
        "x" => EdgeMarker.Cross,
        _ => EdgeMarker.None
    };

    private string ReadText(string close)
    {
        SkipSpaces();
        string text;
        if (!AtEnd && Peek == '"')
        {
            var end = _text.IndexOf('"', _pos + 1);
            if (end < 0) throw Error("Missing closing '\"'.");
            text = _text[(_pos + 1)..end];
            _pos = end + 1;
            SkipSpaces();
            if (!Rest.StartsWith(close, StringComparison.Ordinal)) throw Error($"Expected '{close}'.");
        }
        else
        {
            var end = _text.IndexOf(close, _pos, StringComparison.Ordinal);
            if (end < 0) throw Error($"Missing '{close}'.");
            text = _text[_pos..end];
            _pos = end;
        }

        _pos += close.Length;
        return CleanLabel(text) ?? string.Empty;
    }

    private static string? CleanLabel(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        text = MermaidText.Clean(text);
        return text.Length == 0 ? null : text;
    }

    private bool AtEnd => _pos >= _text.Length;
    private char Peek => _text[_pos];
    private ReadOnlySpan<char> Rest => _text.AsSpan(_pos);

    private void SkipSpaces()
    {
        while (!AtEnd && Peek is ' ' or '\t') _pos++;
    }

    private DiagramSyntaxException Error(string message) => new(message, _lineNumber);
}
