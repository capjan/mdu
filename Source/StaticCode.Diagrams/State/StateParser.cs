using System.Text.RegularExpressions;
using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.Text;

namespace StaticCode.Diagrams.State;

/// <summary>Parses Mermaid states and transitions; composite states have their own local graph.</summary>
public sealed class StateParser
{
    private const string Id = @"[\p{L}\p{N}_][\p{L}\p{N}_-]*";
    private const string Endpoint = @"(?:\[\*\]|" + Id + @")(?:\:\:\:[\w-]+)?";
    private static readonly Regex Header = new(@"^stateDiagram(?:-v2)?\s*;?$", RegexOptions.Compiled);
    private static readonly Regex Transition = new(
        @"^(?<from>" + Endpoint + @")\s*-->\s*(?<to>" + Endpoint + @")(?:\s*:\s*(?<label>.+))?$", RegexOptions.Compiled);
    private static readonly Regex Declaration = new(
        "^state\\s+(?:\"(?<label>[^\"]+)\"\\s+as\\s+)?(?<id>" + Id + ")(?:\\s+<<(?<kind>choice|fork|join)>>)?\\s*(?<open>\\{)?$", RegexOptions.Compiled);
    private static readonly Regex Description = new(@"^(?<id>" + Id + @")\s*:(?!:)\s*(?<label>.+)$", RegexOptions.Compiled);
    private static readonly Regex BareState = new(@"^(?<id>" + Id + @")(?:\:\:\:[\w-]+)?$", RegexOptions.Compiled);
    private static readonly Regex Direction = new(@"^direction\s+(?<value>TB|TD|BT|LR|RL)$", RegexOptions.Compiled);
    private static readonly Regex Click = new(
        "^click\\s+(?<id>" + Id + ")\\s+(?=href\\s|[\"'])(?:href\\s+)?(?:\"(?<doubleUrl>[^\"]*)\"|'(?<singleUrl>[^']*)'|(?<bareUrl>[^ \\t\"']+?))(?:\\s+\"[^\"]*\")?;?$", RegexOptions.Compiled);

    private readonly string[] _lines;
    private readonly Dictionary<string, (StateNode Node, StateDiagram Scope)> _nodes = new(StringComparer.Ordinal);
    private readonly List<(string Id, string Href, int Line)> _links = new();
    private readonly HashSet<StateNode> _labelled = new();
    private int _line;

    private StateParser(string source) => _lines = source.ReplaceLineEndings("\n").Split('\n');

    public static bool CanParse(string source) => Header.IsMatch(source.ReplaceLineEndings("\n").Split('\n')
        .Select(WithoutComment).FirstOrDefault(line => line.Length > 0) ?? string.Empty);

    public static StateDiagram Parse(string source) => new StateParser(source).Parse();

    private StateDiagram Parse()
    {
        while (_line < _lines.Length && WithoutComment(_lines[_line]).Length == 0) _line++;
        if (_line == _lines.Length || !Header.IsMatch(WithoutComment(_lines[_line]))) throw Error("Expected a stateDiagram-v2 or stateDiagram header.");
        _line++;
        var diagram = ParseScope(false);
        foreach (var link in _links)
        {
            if (!_nodes.TryGetValue(link.Id, out var target))
                throw new DiagramSyntaxException($"Unknown state '{link.Id}' in click statement.", link.Line);
            target.Node.Href = link.Href;
        }
        return diagram;
    }

    private StateDiagram ParseScope(bool composite)
    {
        var diagram = new StateDiagram();
        for (; _line < _lines.Length; _line++)
        {
            var line = WithoutComment(_lines[_line]);
            if (line.EndsWith(';') && !Regex.IsMatch(line, @"#[\dA-Za-z]+;$")) line = line[..^1].TrimEnd();
            if (line.Length == 0) continue;
            if (line == "}")
            {
                if (!composite) throw Error("Unexpected '}'.");
                return diagram;
            }
            var direction = Direction.Match(line);
            if (direction.Success)
            {
                diagram.Direction = direction.Groups["value"].Value switch
                {
                    "BT" => FlowDirection.BottomToTop,
                    "LR" => FlowDirection.LeftToRight,
                    "RL" => FlowDirection.RightToLeft,
                    _ => FlowDirection.TopToBottom
                };
                continue;
            }
            if (line.StartsWith("accTitle:", StringComparison.Ordinal))
            {
                diagram.Title = MermaidText.Clean(line[9..]);
                continue;
            }
            if (line.StartsWith("accDescr:", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(line, @"^(?:classDef|class|style)\b")) continue;

            var click = Click.Match(line);
            if (click.Success)
            {
                var url = click.Groups["doubleUrl"].Success ? click.Groups["doubleUrl"].Value
                    : click.Groups["singleUrl"].Success ? click.Groups["singleUrl"].Value
                    : click.Groups["bareUrl"].Value;
                _links.Add((click.Groups["id"].Value, url, _line + 1));
                continue;
            }
            var transition = Transition.Match(line);
            if (transition.Success)
            {
                var from = EndpointNode(diagram, transition.Groups["from"].Value, true);
                var to = EndpointNode(diagram, transition.Groups["to"].Value, false);
                var label = transition.Groups["label"].Success ? Clean(transition.Groups["label"].Value) : null;
                diagram.Edges.Add(new FlowchartEdge(from.Id, to.Id, label, LineStyle.Solid, EdgeMarker.None, EdgeMarker.Arrow, 1));
                continue;
            }
            var declaration = Declaration.Match(line);
            if (declaration.Success)
            {
                var node = GetNode(diagram, declaration.Groups["id"].Value);
                if (declaration.Groups["label"].Success)
                {
                    node.Label = Clean(declaration.Groups["label"].Value);
                    _labelled.Add(node);
                }
                if (declaration.Groups["kind"].Success)
                {
                    if (node.Content != null) throw Error("A composite state cannot be a choice, fork, or join.");
                    node.Kind = declaration.Groups["kind"].Value switch
                    {
                        "choice" => StateKind.Choice,
                        "fork" => StateKind.Fork,
                        _ => StateKind.Join
                    };
                }
                if (declaration.Groups["open"].Success)
                {
                    if (node.Content != null || node.Kind != StateKind.State) throw Error("Invalid or repeated composite state.");
                    _line++;
                    node.Content = ParseScope(true);
                }
                continue;
            }
            var description = Description.Match(line);
            if (description.Success)
            {
                var node = GetNode(diagram, description.Groups["id"].Value);
                var label = Clean(description.Groups["label"].Value);
                node.Label = _labelled.Add(node) ? label : node.Label + "\n" + label;
                continue;
            }
            var bare = BareState.Match(line);
            if (bare.Success)
            {
                GetNode(diagram, bare.Groups["id"].Value);
                continue;
            }
            throw Error($"Unsupported state diagram statement '{line}'.");
        }
        if (composite) throw Error("Unclosed composite state; expected '}'.");
        return diagram;
    }

    private StateNode EndpointNode(StateDiagram diagram, string text, bool source)
    {
        var id = text.Split(":::", 2)[0];
        if (id != "[*]") return GetNode(diagram, id);
        var kind = source ? StateKind.Start : StateKind.End;
        var node = diagram.Nodes.FirstOrDefault(node => node.Kind == kind);
        if (node != null) return node;
        node = new StateNode(source ? "$start" : "$end", kind);
        diagram.Nodes.Add(node);
        return node;
    }

    private StateNode GetNode(StateDiagram scope, string id)
    {
        if (_nodes.TryGetValue(id, out var existing))
        {
            if (existing.Scope != scope) throw Error($"State '{id}' belongs to another composite scope; use unique IDs and connect composite states instead.");
            return existing.Node;
        }
        var node = new StateNode(id);
        scope.Nodes.Add(node);
        _nodes.Add(id, (node, scope));
        return node;
    }

    private static string Clean(string text) => MermaidText.Clean(text.Trim().Trim('"').Replace("\\n", "\n"));

    private static string WithoutComment(string text)
    {
        char quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            if (quote != '\0')
            {
                if (text[index] == quote) quote = '\0';
            }
            else if (text[index] == '"' || text[index] == '\'' && (index == 0 || char.IsWhiteSpace(text[index - 1]))) quote = text[index];
            else if (text[index] == '%' && index + 1 < text.Length && text[index + 1] == '%') return text[..index].Trim();
        }
        return text.Trim();
    }

    private DiagramSyntaxException Error(string message) => new(message, _line + 1);
}
