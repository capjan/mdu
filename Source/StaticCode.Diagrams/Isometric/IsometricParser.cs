using System.Globalization;
using System.Text.RegularExpressions;

namespace StaticCode.Diagrams.Isometric;

/// <summary>
/// Parses isometric diagrams. <c>zone Id "Title" "Subtitle" … end</c> groups nodes on a platform, a line with an
/// identifier and an optional quoted label declares a node, and Mermaid-style links (<c>--&gt;</c>, <c>-.-&gt;</c>,
/// <c>==&gt;</c>) connect nodes. Each <c>phase</c> line starts a phase that contains the links after it.
/// </summary>
internal static partial class IsometricParser
{
    public const int MaxPhases = 9;

    /// <summary>Grid positions run from 0 to one less than this; zones have at most this many columns.</summary>
    public const int MaxGridSize = 100;

    public static readonly IReadOnlySet<string> Tones = new HashSet<string>(StringComparer.Ordinal)
    {
        "blue", "teal", "green", "purple", "orange", "red", "gray"
    };

    [GeneratedRegex(@"^(?<from>[^:]+?)\s*(?<arrow>-\.->|==>|-->)\s*(?<to>[^:]+?)\s*(?::\s*(?<label>.*))?$")]
    private static partial Regex EdgeRegex();

    [GeneratedRegex(@"^[A-Za-z_][\w-]*$")]
    private static partial Regex IdRegex();

    // A whole word, so that node ids such as accTitleServer stay nodes.
    [GeneratedRegex(@"^accTitle(?:\s*:|\s+|$)(?<title>.*)$")]
    private static partial Regex AccTitleRegex();

    [GeneratedRegex(@"^(?<column>[0-9]+)\s*,\s*(?<row>[0-9]+)$")]
    private static partial Regex CellRegex();

    public static IsometricDiagram Parse(string source)
    {
        var zones = new List<IsometricZone>();
        var nodes = new Dictionary<string, IsometricNode>(StringComparer.Ordinal);
        var nodeOrder = new List<IsometricNode>();
        var edges = new List<IsometricEdge>();
        var phases = new List<string>();
        string? title = null;
        IsometricZone? zone = null;

        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;

            if (AccTitleRegex().Match(line) is { Success: true } titleMatch)
            {
                title = titleMatch.Groups["title"].Value.Trim();
                continue;
            }

            // Arrows and colons inside quoted text belong to a label, so the link syntax is matched with quoted text
            // masked and the parts are then read from the original line. Masking needs balanced quotes.
            if (line.Count(character => character == '"') % 2 != 0)
                throw new DiagramSyntaxException("Unterminated quoted text.", lineNumber);

            if (EdgeRegex().Match(MaskQuotedText(line)) is { Success: true } edgeMatch)
            {
                string Part(string name) => line.Substring(edgeMatch.Groups[name].Index, edgeMatch.Groups[name].Length);
                var style = edgeMatch.Groups["arrow"].Value switch
                {
                    "-.->" => IsometricEdgeStyle.Dashed,
                    "==>" => IsometricEdgeStyle.Thick,
                    _ => IsometricEdgeStyle.Solid
                };
                var label = edgeMatch.Groups["label"].Success ? Unquote(Part("label").Trim()) : null;
                var phase = Math.Max(1, phases.Count);
                foreach (var from in Ids(Part("from"), lineNumber))
                foreach (var to in Ids(Part("to"), lineNumber))
                {
                    if (from == to) throw new DiagramSyntaxException($"'{from}' cannot link to itself.", lineNumber);
                    edges.Add(new IsometricEdge(from, to, style, string.IsNullOrEmpty(label) ? null : label, phase));
                }

                continue;
            }

            var tokens = Tokenize(line, lineNumber);
            var keyword = tokens[0];
            if (!keyword.Quoted && keyword.Text == "end")
            {
                if (zone == null) throw new DiagramSyntaxException("'end' without an open zone.", lineNumber);
                zone = null;
            }
            else if (!keyword.Quoted && keyword.Text == "zone")
            {
                if (zone != null) throw new DiagramSyntaxException("Zones cannot be nested; close the open zone with 'end'.", lineNumber);
                zone = ParseZone(tokens, lineNumber);
                if (zones.Any(other => other.Id == zone.Id) || nodes.ContainsKey(zone.Id))
                    throw new DiagramSyntaxException($"'{zone.Id}' is declared twice.", lineNumber);
                zones.Add(zone);
            }
            else if (!keyword.Quoted && keyword.Text == "phase")
            {
                if (zone != null) throw new DiagramSyntaxException("A phase cannot start inside a zone.", lineNumber);
                if (phases.Count == MaxPhases) throw new DiagramSyntaxException($"A diagram can have at most {MaxPhases} phases.", lineNumber);
                var phaseTitle = string.Join(' ', tokens.Skip(1).Select(token => token.Text)).Trim();
                phases.Add(phaseTitle.Length == 0 ? $"Phase {phases.Count + 1}" : phaseTitle);
            }
            else
            {
                var node = ParseNode(tokens, zone, lineNumber);
                if (nodes.ContainsKey(node.Id) || zones.Any(other => other.Id == node.Id))
                    throw new DiagramSyntaxException($"'{node.Id}' is declared twice.", lineNumber);
                nodes.Add(node.Id, node);
                nodeOrder.Add(node);
                zone?.Nodes.Add(node);
            }
        }

        if (zone != null) throw new DiagramSyntaxException($"Zone '{zone.Id}' is missing its 'end'.", lines.Length);

        // Like Mermaid, a link to an undeclared id creates a node with that id as its label.
        foreach (var edge in edges)
        foreach (var id in new[] { edge.From, edge.To })
        {
            if (nodes.ContainsKey(id)) continue;
            if (zones.Any(other => other.Id == id)) throw new DiagramSyntaxException($"Links connect nodes, not the zone '{id}'.", 1);
            var node = new IsometricNode { Id = id, Label = id };
            nodes.Add(id, node);
            nodeOrder.Add(node);
        }

        if (nodeOrder.Count == 0) throw new DiagramSyntaxException("An isometric diagram needs at least one node.", 1);

        foreach (var edge in edges)
        {
            nodes[edge.From].Phase = Math.Min(nodes[edge.From].Phase, edge.Phase);
            nodes[edge.To].Phase = Math.Min(nodes[edge.To].Phase, edge.Phase);
        }

        foreach (var node in nodeOrder.Where(node => node.Phase == int.MaxValue)) node.Phase = 1;

        return new IsometricDiagram(zones, nodeOrder, edges, phases, title);
    }

    private static IsometricZone ParseZone(IReadOnlyList<Token> tokens, int lineNumber)
    {
        var zone = new IsometricZone { Id = ReadId(tokens, 1, lineNumber) };
        var index = 2;
        if (index < tokens.Count && tokens[index].Quoted) zone.Title = tokens[index++].Text;
        if (index < tokens.Count && tokens[index].Quoted) zone.Subtitle = tokens[index++].Text;
        foreach (var (key, value) in Options(tokens, index, lineNumber))
        {
            switch (key)
            {
                case "tone": zone.Tone = ReadTone(value, lineNumber); break;
                case "at": zone.Cell = ReadCell(value, lineNumber); break;
                case "cols": zone.Columns = ReadGridNumber(value, 1, MaxGridSize, "'cols'", lineNumber); break;
                default: throw new DiagramSyntaxException($"Unknown zone option '{key}'.", lineNumber);
            }
        }

        return zone;
    }

    private static IsometricNode ParseNode(IReadOnlyList<Token> tokens, IsometricZone? zone, int lineNumber)
    {
        var id = ReadId(tokens, 0, lineNumber);
        var index = 1;
        var label = index < tokens.Count && tokens[index].Quoted ? tokens[index++].Text : id;
        var node = new IsometricNode { Id = id, Label = label, Zone = zone };
        foreach (var (key, value) in Options(tokens, index, lineNumber))
        {
            switch (key)
            {
                case "icon": node.Icon = value.Trim(':'); break;
                case "tone": node.Tone = ReadTone(value, lineNumber); break;
                case "at": node.Cell = ReadCell(value, lineNumber); break;
                case "shape":
                    node.Shape = value switch
                    {
                        "block" => IsometricShape.Block,
                        "tall" => IsometricShape.Tall,
                        "large" => IsometricShape.Large,
                        "disc" => IsometricShape.Disc,
                        _ => throw new DiagramSyntaxException($"Unknown shape '{value}'; use block, tall, large, or disc.", lineNumber)
                    };
                    break;
                default: throw new DiagramSyntaxException($"Unknown node option '{key}'.", lineNumber);
            }
        }

        return node;
    }

    private static IEnumerable<(string Key, string Value)> Options(IReadOnlyList<Token> tokens, int start, int lineNumber)
    {
        for (var index = start; index < tokens.Count; index += 2)
        {
            if (tokens[index].Quoted) throw new DiagramSyntaxException($"Unexpected text \"{tokens[index].Text}\".", lineNumber);
            if (index + 1 == tokens.Count) throw new DiagramSyntaxException($"Option '{tokens[index].Text}' needs a value.", lineNumber);
            yield return (tokens[index].Text, tokens[index + 1].Text);
        }
    }

    private static string ReadId(IReadOnlyList<Token> tokens, int index, int lineNumber)
    {
        if (index >= tokens.Count || tokens[index].Quoted || !IdRegex().IsMatch(tokens[index].Text))
            throw new DiagramSyntaxException("Expected an id made of letters, digits, '_' or '-'.", lineNumber);
        return tokens[index].Text;
    }

    private static IEnumerable<string> Ids(string list, int lineNumber) =>
        list.Split(',').Select(id => id.Trim()).Select(id => IdRegex().IsMatch(id)
            ? id
            : throw new DiagramSyntaxException($"'{id}' is not a valid id.", lineNumber));

    private static string ReadTone(string value, int lineNumber) =>
        Tones.Contains(value)
            ? value
            : throw new DiagramSyntaxException($"Unknown tone '{value}'; use {string.Join(", ", Tones)}.", lineNumber);

    private static GridCell ReadCell(string value, int lineNumber)
    {
        var match = CellRegex().Match(value);
        if (!match.Success) throw new DiagramSyntaxException($"'at' needs a column and row such as 1,0, not '{value}'.", lineNumber);
        return new GridCell(
            ReadGridNumber(match.Groups["column"].Value, 0, MaxGridSize - 1, "'at'", lineNumber),
            ReadGridNumber(match.Groups["row"].Value, 0, MaxGridSize - 1, "'at'", lineNumber));
    }

    /// <summary>Reads a grid position or size; the limit keeps the layout grid small and the arithmetic in range.</summary>
    private static int ReadGridNumber(string value, int minimum, int maximum, string option, int lineNumber) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new DiagramSyntaxException($"{option} needs numbers from {minimum} to {maximum}, not '{value}'.", lineNumber);

    /// <summary>Replaces the characters between double quotes with spaces of the same length; the quotes stay.</summary>
    private static string MaskQuotedText(string line)
    {
        var masked = line.ToCharArray();
        var quoted = false;
        for (var index = 0; index < masked.Length; index++)
        {
            if (masked[index] == '"') quoted = !quoted;
            else if (quoted) masked[index] = ' ';
        }

        return new string(masked);
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private readonly record struct Token(string Text, bool Quoted);

    private static List<Token> Tokenize(string line, int lineNumber)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < line.Length)
        {
            if (char.IsWhiteSpace(line[index]))
            {
                index++;
                continue;
            }

            if (line[index] == '"')
            {
                var end = line.IndexOf('"', index + 1);
                if (end < 0) throw new DiagramSyntaxException("Unterminated quoted text.", lineNumber);
                tokens.Add(new Token(line[(index + 1)..end], true));
                index = end + 1;
                continue;
            }

            var start = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] != '"') index++;
            tokens.Add(new Token(line[start..index], false));
        }

        return tokens;
    }
}
