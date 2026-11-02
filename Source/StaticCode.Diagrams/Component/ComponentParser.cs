using System.Text.RegularExpressions;

namespace StaticCode.Diagrams.Component;

/// <summary>
/// Parses the component diagram subset of PlantUML: components, interfaces, and the other element keywords, nested
/// containers with braces, links with PlantUML's length and direction rules, notes, and <c>left to right direction</c>.
/// Styling such as <c>skinparam</c> and colors is ignored because the page theme defines the look.
/// </summary>
public sealed partial class ComponentParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const string Name = @"[\p{L}\p{N}_.]+";

    private static readonly Regex KindRegex = KindRegexPattern();

    [GeneratedRegex(
        @"^(?<kind>component|interface|actor|database|node|cloud|folder|frame|package|rectangle|card|artifact|file|queue|storage)\s+(?<rest>.+)$",
        Options)]
    private static partial Regex KindRegexPattern();
    private static readonly Regex DeclarationRegex = DeclarationRegexPattern();

    [GeneratedRegex(
        $@"^(?<name>""[^""]*""|\[[^\]]*\]|:[^:]+:|{Name})(?:\s+as\s+(?<alias>""[^""]*""|\[[^\]]*\]|{Name}))?" +
        @"(?:\s*(?<stereotype><<[^>]+>>))?(?:\s*#\S+)?\s*(?<brace>\{)?\s*$",
        Options)]
    private static partial Regex DeclarationRegexPattern();
    private static readonly Regex ReferenceRegex = ReferenceRegexPattern();

    [GeneratedRegex(
        $@"\G(?:\[(?<component>[^\]]+)\]|\(\)\s*""(?<quotedInterface>[^""]+)""|\(\)\s*(?<interface>{Name})|:(?<actor>[^:]+):|(?<id>{Name}))",
        Options)]
    private static partial Regex ReferenceRegexPattern();
    private static readonly Regex AliasRegex = AliasRegexPattern();

    [GeneratedRegex($@"\G\s+as\s+(?<alias>{Name})", Options)]
    private static partial Regex AliasRegexPattern();
    private static readonly Regex StereotypeRegex = StereotypeRegexPattern();

    [GeneratedRegex(@"\G\s*(?<stereotype><<[^>]+>>)", Options)]
    private static partial Regex StereotypeRegexPattern();
    private static readonly Regex LinkRegex = LinkRegexPattern();

    [GeneratedRegex(
        @"\G\s*(?<from><\||<)?(?<a>[-.=]+)(?:\[(?<style>[^\]]*)\])?(?:(?<direction>up|down|left|right|do|le|ri|u|d|l|r)(?=[-.=]))?" +
        @"(?<b>[-.=]*)(?:\[(?<style2>[^\]]*)\])?(?<to>\|>|>)?\s*",
        Options)]
    private static partial Regex LinkRegexPattern();
    private static readonly Regex LabelRegex = LabelRegexPattern();

    [GeneratedRegex(@"\G\s*:\s*(?<label>.*)$", Options)]
    private static partial Regex LabelRegexPattern();
    private static readonly Regex NoteRegex = NoteRegexPattern();

    [GeneratedRegex(
        $@"^note\s+(?<position>left|right|top|bottom)\s+of\s+(?<target>\[[^\]]+\]|{Name})\s*(?:#\S+)?\s*(?::\s*(?<text>.*))?$",
        Options)]
    private static partial Regex NoteRegexPattern();
    private static readonly Regex FloatingNoteRegex = FloatingNoteRegexPattern();

    [GeneratedRegex($@"^note\s+(?:""(?<text>[^""]*)""\s+)?as\s+(?<id>{Name})\s*(?:#\S+)?$", Options)]
    private static partial Regex FloatingNoteRegexPattern();
    private static readonly Regex EndNoteRegex = EndNoteRegexPattern();

    [GeneratedRegex(@"^end\s?note$", Options)]
    private static partial Regex EndNoteRegexPattern();
    private static readonly Regex TitleRegex = TitleRegexPattern();

    [GeneratedRegex(@"^title\s+(?<title>.+)$", Options)]
    private static partial Regex TitleRegexPattern();
    private static readonly Regex TogetherRegex = TogetherRegexPattern();

    [GeneratedRegex(@"^together\s*\{$", Options)]
    private static partial Regex TogetherRegexPattern();
    private static readonly Regex IgnoredRegex = IgnoredRegexPattern();

    [GeneratedRegex(@"^(?:skinparam|hide|show|scale|!theme|!pragma|allowmixing|allow_mixing)\b", Options)]
    private static partial Regex IgnoredRegexPattern();
    private static readonly Regex DirectionRegex = DirectionRegexPattern();

    [GeneratedRegex(@"^(?<direction>left to right|top to bottom) direction$", Options)]
    private static partial Regex DirectionRegexPattern();
    private static readonly Regex UrlRegex = UrlRegexPattern();

    [GeneratedRegex($@"^url\s+of\s+(?<id>{Name})\s+is\s+\[\[(?<url>[^\]]+)\]\]$", Options)]
    private static partial Regex UrlRegexPattern();

    private readonly List<string> _lines;
    private readonly Dictionary<string, ComponentElement> _elements = new(StringComparer.Ordinal);
    private readonly List<ComponentElement> _elementOrder = new();
    private readonly List<ComponentLink> _links = new();
    private readonly List<(string Id, string Href)> _elementLinks = new();

    // Open braces: a container id, or null for a group such as "together" that draws nothing.
    private readonly Stack<string?> _blocks = new();
    private bool _leftToRight;
    private bool _hasComponentSyntax;
    private string? _title;
    private int _noteCount;
    private int _anonymousCount;
    private int _lineNumber;

    private ComponentParser(string source)
    {
        _lines = RemoveBlockComments(source.ReplaceLineEndings("\n").Split('\n'));
    }

    public static ComponentDiagram Parse(string source) => new ComponentParser(source).Parse();

    private ComponentDiagram Parse()
    {
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index].Trim();
            _lineNumber = index + 1;
            if (line.Length == 0 || line.StartsWith('\'') || line.StartsWith('@')) continue;

            if (IgnoredRegex.IsMatch(line))
            {
                if (line.EndsWith('{')) index = SkipBlock(index);
                continue;
            }

            if (line.StartsWith("note", StringComparison.Ordinal) && TryParseNote(line, ref index)) continue;
            ParseStatement(line);
        }

        if (_blocks.Count > 0) throw Error("Missing '}'.");
        if (!_hasComponentSyntax) throw new DiagramSyntaxException("This is not a component diagram.", 1);
        foreach (var (id, href) in _elementLinks)
            if (_elements.TryGetValue(id, out var element)) element.Href = href;
        return new ComponentDiagram(_title, _leftToRight, _elementOrder, _links);
    }

    private void ParseStatement(string line)
    {
        if (line == "}")
        {
            if (_blocks.Count == 0) throw Error("'}' without an open block.");
            _blocks.Pop();
            return;
        }

        if (TitleRegex.Match(line) is { Success: true } title)
        {
            _title = CleanText(title.Groups["title"].Value);
            return;
        }

        if (DirectionRegex.Match(line) is { Success: true } direction)
        {
            _leftToRight = direction.Groups["direction"].Value == "left to right";
            return;
        }

        if (UrlRegex.Match(line) is { Success: true } url)
        {
            _elementLinks.Add((url.Groups["id"].Value, url.Groups["url"].Value));
            return;
        }

        if (TogetherRegex.IsMatch(line))
        {
            _blocks.Push(null);
            return;
        }

        if (KindRegex.Match(line) is { Success: true } kind && TryParseDeclaration(kind)) return;
        ParseReferenceStatement(line);
    }

    private bool TryParseDeclaration(Match kindMatch)
    {
        var kindName = kindMatch.Groups["kind"].Value;
        if (kindMatch.Groups["rest"].Value.Trim() == "{")
        {
            // An unnamed container such as "cloud {".
            var anonymous = Declare($"{kindName}-{++_anonymousCount}", Enum.Parse<ElementKind>(kindName, ignoreCase: true), string.Empty, null);
            anonymous.IsContainer = true;
            _blocks.Push(anonymous.Id);
            _hasComponentSyntax = true;
            return true;
        }

        var declaration = DeclarationRegex.Match(kindMatch.Groups["rest"].Value);
        if (!declaration.Success) return false;

        var kind = Enum.Parse<ElementKind>(kindMatch.Groups["kind"].Value, ignoreCase: true);
        var name = declaration.Groups["name"].Value;
        var alias = declaration.Groups["alias"].Success ? declaration.Groups["alias"].Value : null;
        var nameIsLabel = IsDecorated(name) || alias == null || !IsDecorated(alias);
        var id = alias == null ? Undecorate(name) : nameIsLabel ? Undecorate(alias) : name;
        var label = nameIsLabel ? Undecorate(name) : Undecorate(alias!);

        var element = Declare(id, kind, label, declaration.Groups["stereotype"].Value);
        if (kind is not (ElementKind.Actor or ElementKind.Note)) _hasComponentSyntax = true;
        if (declaration.Groups["brace"].Success)
        {
            element.IsContainer = true;
            _blocks.Push(element.Id);
        }

        return true;
    }

    private static bool IsDecorated(string name) => name.Length > 0 && name[0] is '"' or '[' or ':';

    private static string Undecorate(string name) => IsDecorated(name) ? CleanText(name[1..^1]) : name;

    /// <summary>A statement that starts with an element: a shorthand declaration such as <c>[A] as B</c>, or a link.</summary>
    private void ParseReferenceStatement(string line)
    {
        var position = 0;
        var from = ParseReference(line, ref position, allowAlias: true);
        if (position >= line.Length || line[position..].Trim() is "{" || line[position..].TrimStart().StartsWith('#'))
        {
            if (line[position..].Trim() == "{")
            {
                from.IsContainer = true;
                _blocks.Push(from.Id);
            }

            return;
        }

        var link = LinkRegex.Match(line, position);
        if (!link.Success || link.Length == 0 || link.Groups["a"].Length == 0)
            throw Error($"Expected a link at '{line[position..].Trim()}'.");
        position += link.Length;

        var to = ParseReference(line, ref position, allowAlias: false);
        string? label = null;
        if (position < line.Length)
        {
            var labelMatch = LabelRegex.Match(line, position);
            if (!labelMatch.Success) throw Error($"Unexpected '{line[position..].Trim()}'.");
            label = CleanText(labelMatch.Groups["label"].Value);
            if (label.Length == 0) label = null;
        }

        if (from == to) throw Error("Links from an element to itself are not supported yet.");
        _links.Add(CreateLink(from.Id, to.Id, label, link));
    }

    private ComponentElement ParseReference(string line, ref int position, bool allowAlias)
    {
        var match = ReferenceRegex.Match(line, position);
        if (!match.Success) throw Error($"Expected an element at '{line[position..].Trim()}'.");
        position += match.Length;

        // PlantUML accepts the alias and the stereotype in either order.
        string? alias = null;
        string? stereotype = null;
        for (var part = 0; part < 2; part++)
        {
            if (allowAlias && alias == null && AliasRegex.Match(line, position) is { Success: true } aliasMatch)
            {
                alias = aliasMatch.Groups["alias"].Value;
                position += aliasMatch.Length;
            }
            else if (stereotype == null && StereotypeRegex.Match(line, position) is { Success: true } stereotypeMatch)
            {
                stereotype = stereotypeMatch.Groups["stereotype"].Value;
                position += stereotypeMatch.Length;
            }
        }

        if (match.Groups["component"].Success)
        {
            _hasComponentSyntax = true;
            var label = CleanText(match.Groups["component"].Value);
            var existing = alias == null ? FindByLabel(label, ElementKind.Component) : null;
            return existing ?? Declare(alias ?? label, ElementKind.Component, label, stereotype, onlyIfNew: alias == null);
        }

        if (match.Groups["quotedInterface"].Success || match.Groups["interface"].Success)
        {
            _hasComponentSyntax = true;
            var label = CleanText(match.Groups["quotedInterface"].Success ? match.Groups["quotedInterface"].Value : match.Groups["interface"].Value);
            return Declare(alias ?? label, ElementKind.Interface, label, stereotype, onlyIfNew: alias == null);
        }

        if (match.Groups["actor"].Success)
        {
            var label = CleanText(match.Groups["actor"].Value);
            return Declare(alias ?? label, ElementKind.Actor, label, stereotype, onlyIfNew: alias == null);
        }

        // PlantUML turns an unknown plain name in a component diagram into an interface.
        var id = match.Groups["id"].Value;
        if (alias != null) throw Error($"'{id} as {alias}' needs a keyword such as 'component'.");
        return _elements.TryGetValue(id, out var element) ? element : Declare(id, ElementKind.Interface, id, stereotype);
    }

    private ComponentLink CreateLink(string from, string to, string? label, Match link)
    {
        var lineChars = link.Groups["a"].Value + link.Groups["b"].Value;
        var style = $"{link.Groups["style"].Value},{link.Groups["style2"].Value}"
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant())
            .ToList();

        var line = lineChars[0] switch { '.' => LinkLine.Dotted, '=' => LinkLine.Bold, _ => LinkLine.Solid };
        if (style.Contains("dashed") || style.Contains("dotted")) line = LinkLine.Dotted;
        if (style.Contains("bold") || style.Any(item => item.StartsWith("thickness=", StringComparison.Ordinal) && item != "thickness=1"))
            line = LinkLine.Bold;

        var direction = link.Groups["direction"].Value switch
        {
            "up" or "u" => LinkDirection.Up,
            "down" or "do" or "d" => LinkDirection.Down,
            "left" or "le" or "l" => LinkDirection.Left,
            "right" or "ri" or "r" => LinkDirection.Right,
            _ => lineChars.Length == 1 ? LinkDirection.Right : LinkDirection.Down
        };

        // In "left to right direction", ranks run from left to right, so explicit directions turn by 90 degrees.
        if (_leftToRight && link.Groups["direction"].Success)
        {
            direction = direction switch
            {
                LinkDirection.Right => LinkDirection.Down,
                LinkDirection.Left => LinkDirection.Up,
                LinkDirection.Down => LinkDirection.Right,
                _ => LinkDirection.Left
            };
        }

        return new ComponentLink(
            from,
            to,
            label,
            line,
            Head(link.Groups["from"].Value),
            Head(link.Groups["to"].Value),
            direction,
            Math.Max(1, lineChars.Length - 1),
            style.Contains("hidden"));
    }

    private static LinkHead Head(string value) => value switch
    {
        "<" or ">" => LinkHead.Arrow,
        "<|" or "|>" => LinkHead.Triangle,
        _ => LinkHead.None
    };

    private bool TryParseNote(string line, ref int index)
    {
        string id;
        string text;
        if (NoteRegex.Match(line) is { Success: true } attached)
        {
            var position = 0;
            var targetText = attached.Groups["target"].Value;
            var target = ParseReference(targetText, ref position, allowAlias: false);
            text = attached.Groups["text"].Success ? attached.Groups["text"].Value : ReadNoteBody(ref index);
            id = $"note-{++_noteCount}";
            var note = Declare(id, ElementKind.Note, CleanText(text), null);
            _links.Add(attached.Groups["position"].Value switch
            {
                "left" => NoteLink(note.Id, target.Id, LinkDirection.Right),
                "right" => NoteLink(target.Id, note.Id, LinkDirection.Right),
                "top" => NoteLink(note.Id, target.Id, LinkDirection.Down),
                _ => NoteLink(target.Id, note.Id, LinkDirection.Down)
            });
            return true;
        }

        if (FloatingNoteRegex.Match(line) is not { Success: true } floating) return false;
        id = floating.Groups["id"].Value;
        text = floating.Groups["text"].Success ? floating.Groups["text"].Value : ReadNoteBody(ref index);
        Declare(id, ElementKind.Note, CleanText(text), null);
        return true;
    }

    private static ComponentLink NoteLink(string from, string to, LinkDirection direction) =>
        new(from, to, null, LinkLine.Dotted, LinkHead.None, LinkHead.None, direction);

    private string ReadNoteBody(ref int index)
    {
        var body = new List<string>();
        for (index++; index < _lines.Count; index++)
        {
            var line = _lines[index].Trim();
            if (EndNoteRegex.IsMatch(line)) return string.Join("\\n", body);
            body.Add(line);
        }

        throw Error("Missing 'end note'.");
    }

    private ComponentElement Declare(string id, ElementKind kind, string label, string? stereotype, bool onlyIfNew = false)
    {
        if (_elements.TryGetValue(id, out var element))
        {
            if (onlyIfNew) return element;
            element.Kind = kind;
            element.Label = label;
        }
        else
        {
            element = new ComponentElement(id, kind, _blocks.FirstOrDefault(block => block != null)) { Label = label };
            _elements.Add(id, element);
            _elementOrder.Add(element);
        }

        if (!string.IsNullOrEmpty(stereotype)) element.Stereotype = stereotype[2..^2].Trim();
        return element;
    }

    private ComponentElement? FindByLabel(string label, ElementKind kind) =>
        _elements.TryGetValue(label, out var byId) ? byId : _elementOrder.FirstOrDefault(element => element.Kind == kind && element.Label == label);

    private int SkipBlock(int index)
    {
        var depth = 0;
        for (; index < _lines.Count; index++)
        {
            var line = _lines[index].Trim();
            if (line.EndsWith('{')) depth++;
            if (line == "}" && --depth == 0) return index;
        }

        throw Error("Missing '}'.");
    }

    /// <summary>Decodes PlantUML's <c>\n</c> line breaks.</summary>
    private static string CleanText(string text) =>
        string.Join('\n', text.Replace("\\n", "\n").Split('\n').Select(line => line.Trim())).Trim();

    /// <summary>Blanks out <c>/' ... '/</c> comments while keeping line numbers.</summary>
    private static List<string> RemoveBlockComments(string[] lines)
    {
        var result = new List<string>(lines.Length);
        var inComment = false;
        foreach (var line in lines)
        {
            var text = line;
            var kept = new System.Text.StringBuilder();
            while (text.Length > 0)
            {
                if (inComment)
                {
                    var end = text.IndexOf("'/", StringComparison.Ordinal);
                    if (end < 0) break;
                    text = text[(end + 2)..];
                    inComment = false;
                }
                else
                {
                    var start = text.IndexOf("/'", StringComparison.Ordinal);
                    if (start < 0)
                    {
                        kept.Append(text);
                        break;
                    }

                    kept.Append(text[..start]);
                    text = text[(start + 2)..];
                    inComment = true;
                }
            }

            result.Add(kept.ToString());
        }

        return result;
    }

    private DiagramSyntaxException Error(string message) => new(message, _lineNumber);
}
