using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Extensions.ParserRelated;
using Core.Parser;
using Core.Parser.Special;

namespace StaticCode.Highlighter;

public class Lexer
{
    private static readonly ConcurrentDictionary<Func<Grammar>, Lazy<Lexer>> Cache = new();
    private static readonly Regex NumberRegex = new(
        @"(?<![\p{L}\p{N}_$#])\d+(?:\.\d+)?",
        RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(
        """</?(?<tag>[A-Za-z][A-Za-z0-9:-]*)(?:[^>"']|"[^"]*"|'[^']*')*>""",
        RegexOptions.Compiled);
    private static readonly Regex HtmlAttributeRegex = new(
        """(?<name>[A-Za-z_:][A-Za-z0-9:_.-]*)(?:\s*=\s*(?<value>"[^"]*"|'[^']*'|[^\s>]+))?""",
        RegexOptions.Compiled);
    private static readonly Regex YamlMappingKeyRegex = new(
        "^(?<indent>[ \\t]*)(?:-[ \\t]+)?(?<key>\"(?:\\\\.|[^\"])*\"|'(?:''|[^'])*'|[^:#\\r\\n][^:#\\r\\n]*?):(?=[ \\t]|$)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownHeadingRegex = new(
        @"(?m)^[ \t]{0,3}(?<marker>#{1,6})(?<spacing>[ \t]+)(?<heading>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownBlockMarkerRegex = new(
        @"(?m)^[ \t]{0,3}(?:>[ \t]?|[-+*](?=[ \t])|\d{1,9}[.)](?=[ \t])|`{3,}[^`\r\n]*|~{3,}[^~\r\n]*)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownHorizontalRuleRegex = new(
        @"(?m)^[ \t]{0,3}(?:(?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})[ \t]*$",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownTaskRegex = new(
        @"(?m)^[ \t]{0,3}(?:[-+*]|\d+[.)])[ \t]+\[(?: |x|X)\]",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(
        @"(?<image>!?)[\[](?<label>(?:\\.|[^\]])+)\]\((?<url>(?:\\.|[^)])*)\)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownStrongRegex = new(
        @"(?<marker>\*\*|__)(?<content>\S(?:[^\r\n]*?\S)?)\k<marker>",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownEmphasisRegex = new(
        @"(?<marker>(?<!\*)\*(?!\*)|(?<!_)_(?!_))(?<content>\S(?:[^\r\n]*?\S)?)\k<marker>(?!\*|_)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownCodeRegex = new(
        @"(?<marker>`+)(?<content>[^`\r\n]+?)\k<marker>",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownFrontMatterRegex = new(
        @"\A---[ \t]*\r?\n(?:(?<body>[\s\S]*?)\r?\n)?---[ \t]*(?=\r?\n|\z)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownAlertRegex = new(
        @"\[!(?<kind>NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Lexer ForLanguage(string name)
    {
        return Cache.GetOrAdd(GrammarStore.GetFactory(name), factory => new Lazy<Lexer>(
            () => new Lexer(factory()),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
    private readonly Regex? _keywordRegex;
    private readonly Regex? _declarationKeywordRegex;
    private readonly Regex? _atRuleRegex;
    private readonly LanguageString[] _strings;
    private readonly string _lineCommentStart;
    private readonly bool _highlightKeys;
    private readonly bool _highlightShellTokens;
    private readonly bool _highlightMarkdown;
    private readonly bool _highlightHtml;
    private readonly bool _highlightNumbers;
    private readonly string? _blockCommentStart;
    private readonly string? _blockCommentEnd;

    private Lexer(Grammar settings)
    {
        _highlightKeys = settings.HighlightKeys;
        _highlightShellTokens = settings.HighlightShellTokens;
        _highlightMarkdown = settings.HighlightMarkdown;
        _highlightHtml = settings.HighlightHtml;
        _lineCommentStart = settings.LineCommentStart;
        _blockCommentStart = settings.BlockCommentStart;
        _blockCommentEnd = settings.BlockCommentEnd;
        _highlightNumbers = settings.Keywords.Count > 0 ||
                            settings.DeclarationKeywords.Count > 0 ||
                            settings.AtRules.Count > 0;
        var contextualKeywordOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (settings.IgnoreKeywordCase)
            contextualKeywordOptions |= RegexOptions.IgnoreCase;
        if (settings.DeclarationKeywords.Count > 0)
        {
            var keywords = string.Join("|", settings.DeclarationKeywords
                .OrderByDescending(keyword => keyword.Length)
                .Select(Regex.Escape));
            _declarationKeywordRegex = new Regex(
                $@"(?:^|[;{{])\s*(?:/\*[\s\S]*?\*/\s*)*(?<keyword>{keywords})\s*:",
                contextualKeywordOptions | RegexOptions.Multiline);
        }
        if (settings.AtRules.Count > 0)
        {
            var atRules = string.Join("|", settings.AtRules
                .OrderByDescending(keyword => keyword.Length)
                .Select(Regex.Escape));
            _atRuleRegex = new Regex(
                $@"@(?<keyword>{atRules})(?![\p{{L}}\p{{N}}_-])",
                contextualKeywordOptions);
        }
        if (settings.Keywords.Count > 0)
        {
            const string boundary = @"[\p{L}\p{N}_$]";
            var keywords = string.Join("|", settings.Keywords
                .OrderByDescending(keyword => keyword.Length)
                .Select(Regex.Escape));
            var options = RegexOptions.Compiled;
            if (settings.IgnoreKeywordCase)
                options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            _keywordRegex = new Regex($@"(?<!{boundary})(?:{keywords})(?!{boundary})", options);
        }
        _strings = settings.Strings.OrderByDescending(rule => rule.Delimiter.Length).ToArray();
        _operators = settings.Operators.ToArray();
    }

    private readonly string[] _operators;

    public IEnumerable<LanguageToken> Analyse(TextReader reader)
    {
        var source = reader.ReadToEnd();
        IEnumerable<LanguageToken> tokens = _highlightHtml
            ? AnalyseHtml(source)
            : AnalyseWithLexicalSegments(source);
        tokens = ApplyTokenRanges(
            tokens,
            source.Length,
            FindKeywordAndNumberRanges(source),
            onlyText: true);

        if (_highlightKeys)
            tokens = ApplyTokenRanges(
                tokens,
                source.Length,
                FindYamlKeyRanges(source).Select(range => (range.Start, range.End, TokenType.Key)).ToList());
        if (_highlightShellTokens)
            tokens = ApplyTokenRanges(tokens, source.Length, FindShellTokenRanges(source));
        if (_highlightMarkdown)
        {
            tokens = ApplyTokenRanges(tokens, source.Length, FindMarkdownRanges(source));
            tokens = ApplyTokenRanges(tokens, source.Length, FindFrontMatterKeyRanges(source));
        }

        return tokens;
    }

    private IEnumerable<LanguageToken> AnalyseWithLexicalSegments(string source)
    {
        var segmentStart = 0;
        var position = 0;
        while (position < source.Length)
        {
            if (TryReadComment(source, position, out var commentEnd))
            {
                foreach (var token in AnalyseCore(new StringReader(source[segmentStart..position])))
                    yield return token;
                yield return new LanguageToken(source[position..commentEnd], TokenType.Comment);
                position = commentEnd;
                segmentStart = position;
                continue;
            }

            if (TryReadString(source, position, out var stringEnd))
            {
                foreach (var token in AnalyseCore(new StringReader(source[segmentStart..position])))
                    yield return token;
                yield return new LanguageToken(source[position..stringEnd], TokenType.String);
                position = stringEnd;
                segmentStart = position;
                continue;
            }

            position++;
        }

        foreach (var token in AnalyseCore(new StringReader(source[segmentStart..])))
            yield return token;
    }

    private bool TryReadComment(string source, int position, out int end)
    {
        if (_lineCommentStart.Length > 0 && source.AsSpan(position).StartsWith(_lineCommentStart, StringComparison.Ordinal))
        {
            end = position + _lineCommentStart.Length;
            while (end < source.Length && source[end] is not ('\r' or '\n'))
                end++;
            return true;
        }

        if (_blockCommentStart is not null && _blockCommentEnd is not null &&
            source.AsSpan(position).StartsWith(_blockCommentStart, StringComparison.Ordinal))
        {
            var commentEnd = source.IndexOf(_blockCommentEnd, position + _blockCommentStart.Length, StringComparison.Ordinal);
            end = commentEnd < 0 ? source.Length : commentEnd + _blockCommentEnd.Length;
            return true;
        }

        end = position;
        return false;
    }

    private bool TryReadString(string source, int position, out int end)
    {
        foreach (var rule in _strings)
        {
            if (!source.AsSpan(position).StartsWith(rule.Delimiter, StringComparison.Ordinal))
                continue;

            var contentPosition = position + rule.Delimiter.Length;
            if (rule.IsCharacterLiteral)
            {
                if (TryReadCharacterContent(source, contentPosition, rule, out var closingPosition) &&
                    source.AsSpan(closingPosition).StartsWith(rule.Delimiter, StringComparison.Ordinal))
                {
                    end = closingPosition + rule.Delimiter.Length;
                    return true;
                }

                continue;
            }

            while (contentPosition < source.Length)
            {
                var escape = rule.EscapeSequences.FirstOrDefault(sequence =>
                    source.AsSpan(contentPosition).StartsWith(sequence, StringComparison.Ordinal));
                if (escape is not null)
                    contentPosition += escape.Length;
                else if (source.AsSpan(contentPosition).StartsWith(rule.Delimiter, StringComparison.Ordinal))
                {
                    end = contentPosition + rule.Delimiter.Length;
                    return true;
                }
                else
                    contentPosition++;
            }

            end = position;
            continue;
        }

        end = position;
        return false;
    }

    private static bool TryReadCharacterContent(
        string source,
        int position,
        LanguageString rule,
        out int end)
    {
        if (position >= source.Length || source[position] is '\r' or '\n')
        {
            end = position;
            return false;
        }

        if (source[position] == '\\')
        {
            if (position + 2 < source.Length && source[position + 1] == 'u')
            {
                if (source[position + 2] == '{')
                {
                    var braceEnd = source.IndexOf('}', position + 3);
                    if (braceEnd > position + 3)
                    {
                        end = braceEnd + 1;
                        return true;
                    }
                }
                else if (position + 6 <= source.Length &&
                         char.IsAsciiHexDigit(source[position + 2]) &&
                         char.IsAsciiHexDigit(source[position + 3]) &&
                         char.IsAsciiHexDigit(source[position + 4]) &&
                         char.IsAsciiHexDigit(source[position + 5]))
                {
                    end = position + 6;
                    return true;
                }
            }

            var escape = rule.EscapeSequences.FirstOrDefault(sequence =>
                source.AsSpan(position).StartsWith(sequence, StringComparison.Ordinal));
            if (escape is not null)
                end = position + escape.Length;
            else if (position + 1 < source.Length)
                end = position + 2;
            else
            {
                end = position;
                return false;
            }
        }
        else
            end = char.IsHighSurrogate(source[position]) && position + 1 < source.Length && char.IsLowSurrogate(source[position + 1])
                ? position + 2
                : position + 1;

        return true;
    }

    private IEnumerable<LanguageToken> AnalyseHtml(string source)
    {
        var textStart = 0;
        var position = 0;
        string? rawElement = null;

        while (position < source.Length)
        {
            if (rawElement is not null)
            {
                position = FindHtmlClosingTag(source, position, rawElement);
                if (position < 0)
                    break;
                rawElement = null;
            }

            if (source.AsSpan(position).StartsWith(_blockCommentStart!, StringComparison.Ordinal))
            {
                foreach (var token in AnalyseCore(new StringReader(source[textStart..position])))
                    yield return token;
                var commentEnd = source.IndexOf(_blockCommentEnd!, position + _blockCommentStart!.Length, StringComparison.Ordinal);
                var end = commentEnd < 0 ? source.Length : commentEnd + _blockCommentEnd!.Length;
                yield return new LanguageToken(source[position..end], TokenType.Comment);
                position = end;
                textStart = end;
                continue;
            }

            if (TryReadHtmlTag(source, position, out var tagEnd, out var tagName, out var closingTag, out var selfClosingTag))
            {
                foreach (var token in AnalyseCore(new StringReader(source[textStart..position])))
                    yield return token;

                var tag = source[position..tagEnd];
                var tagTokens = ApplyTokenRanges(
                    AnalyseCore(new StringReader(tag)),
                    tag.Length,
                    FindHtmlRanges(tag));
                foreach (var token in tagTokens)
                    yield return token;

                position = tagEnd;
                textStart = position;
                if (!closingTag && !selfClosingTag && tagName is "script" or "style")
                    rawElement = tagName;
                continue;
            }

            position++;
        }

        foreach (var token in AnalyseCore(new StringReader(source[textStart..])))
            yield return token;
    }

    private static bool TryReadHtmlTag(
        string source,
        int start,
        out int end,
        out string tagName,
        out bool closingTag,
        out bool selfClosingTag)
    {
        end = start;
        tagName = string.Empty;
        closingTag = false;
        selfClosingTag = false;
        if (start >= source.Length || source[start] != '<')
            return false;

        var position = start + 1;
        if (position < source.Length && source[position] == '/')
        {
            closingTag = true;
            position++;
        }

        var nameStart = position;
        while (position < source.Length && (char.IsAsciiLetterOrDigit(source[position]) || source[position] is ':' or '-' or '_'))
            position++;
        if (position == nameStart || !char.IsAsciiLetter(source[nameStart]))
            return false;
        tagName = source[nameStart..position].ToLowerInvariant();

        var quote = '\0';
        for (; position < source.Length; position++)
        {
            var character = source[position];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
            }
            else if (character is '"' or '\'')
                quote = character;
            else if (character == '>')
            {
                end = position + 1;
                var tagContentEnd = position - 1;
                while (tagContentEnd >= start && char.IsWhiteSpace(source[tagContentEnd]))
                    tagContentEnd--;
                selfClosingTag = tagContentEnd >= start && source[tagContentEnd] == '/';
                return true;
            }
        }

        return false;
    }

    private static int FindHtmlClosingTag(string source, int start, string tagName)
    {
        var position = start;
        while ((position = source.IndexOf("</", position, StringComparison.Ordinal)) >= 0)
        {
            var nameStart = position + 2;
            while (nameStart < source.Length && char.IsWhiteSpace(source[nameStart]))
                nameStart++;
            if (source.AsSpan(nameStart).StartsWith(tagName, StringComparison.OrdinalIgnoreCase))
            {
                var boundary = nameStart + tagName.Length;
                if (boundary == source.Length || source[boundary] is '>' or '/' || char.IsWhiteSpace(source[boundary]))
                    return position;
            }

            position += 2;
        }

        return -1;
    }

    private List<(int Start, int End, TokenType Type)> FindKeywordAndNumberRanges(string source)
    {
        var ranges = new List<(int Start, int End, TokenType Type)>();
        if (_keywordRegex is not null)
            ranges.AddRange(FindRegexRanges(_keywordRegex, source, TokenType.Keyword));
        if (_declarationKeywordRegex is not null)
            ranges.AddRange(FindRegexGroupRanges(_declarationKeywordRegex, source, "keyword", TokenType.Keyword));
        if (_atRuleRegex is not null)
            ranges.AddRange(FindRegexGroupRanges(_atRuleRegex, source, "keyword", TokenType.Keyword));

        if (_highlightNumbers)
            ranges.AddRange(FindRegexRanges(NumberRegex, source, TokenType.Number));

        return ranges;
    }

    private static List<(int Start, int End, TokenType Type)> FindRegexRanges(Regex regex, string source, TokenType type)
    {
        return regex.Matches(source)
            .Select(match => (match.Index, match.Index + match.Length, type))
            .ToList();
    }

    private static List<(int Start, int End, TokenType Type)> FindRegexGroupRanges(
        Regex regex,
        string source,
        string groupName,
        TokenType type)
    {
        return regex.Matches(source)
            .Select(match => match.Groups[groupName])
            .Where(group => group.Success)
            .Select(group => (group.Index, group.Index + group.Length, type))
            .ToList();
    }

    private static List<(int Start, int End, TokenType Type)> FindHtmlRanges(string source)
    {
        var ranges = new List<(int Start, int End, TokenType Type)>();
        foreach (Match tagMatch in HtmlTagRegex.Matches(source))
        {
            var tag = tagMatch.Groups["tag"];
            ranges.Add((tag.Index, tag.Index + tag.Length, TokenType.Keyword));

            var attributesStart = tag.Index + tag.Length;
            var attributesLength = tagMatch.Index + tagMatch.Length - attributesStart;
            var attributes = source.Substring(attributesStart, attributesLength);
            foreach (Match attributeMatch in HtmlAttributeRegex.Matches(attributes))
            {
                var name = attributeMatch.Groups["name"];
                ranges.Add((attributesStart + name.Index, attributesStart + name.Index + name.Length, TokenType.Keyword));
                var value = attributeMatch.Groups["value"];
                if (value.Success && value.Value.Length >= 2 && value.Value[0] is '"' or '\'')
                    ranges.Add((attributesStart + value.Index, attributesStart + value.Index + value.Length, TokenType.String));
            }
        }

        return ranges;
    }

    private static List<(int Start, int End, TokenType Type)> FindMarkdownRanges(string source)
    {
        var ranges = new List<(int Start, int End, TokenType Type)>();

        foreach (Match match in MarkdownLinkRegex.Matches(source))
        {
            var image = match.Groups["image"];
            var label = match.Groups["label"];
            var url = match.Groups["url"];
            ranges.Add((match.Index, match.Index + image.Length + 1, TokenType.MarkdownSyntax));
            ranges.Add((label.Index, label.Index + label.Length, TokenType.MarkdownLink));
            ranges.Add((label.Index + label.Length, label.Index + label.Length + 2, TokenType.MarkdownSyntax));
            if (url.Length > 0)
                ranges.Add((url.Index, url.Index + url.Length, TokenType.MarkdownUrl));
            ranges.Add((url.Index + url.Length, url.Index + url.Length + 1, TokenType.MarkdownSyntax));
        }

        AddDelimitedRanges(source, MarkdownStrongRegex, TokenType.MarkdownStrong, ranges);
        AddDelimitedRanges(source, MarkdownEmphasisRegex, TokenType.MarkdownEmphasis, ranges);

        foreach (Match match in MarkdownHeadingRegex.Matches(source))
        {
            var marker = match.Groups["marker"];
            var heading = match.Groups["heading"];
            ranges.Add((marker.Index, marker.Index + marker.Length, TokenType.MarkdownSyntax));
            if (heading.Length > 0)
                ranges.Add((heading.Index, heading.Index + heading.Length, TokenType.MarkdownHeading));
        }

        foreach (Match match in MarkdownBlockMarkerRegex.Matches(source))
            ranges.Add((match.Index, match.Index + match.Length, TokenType.MarkdownSyntax));

        foreach (Match match in MarkdownHorizontalRuleRegex.Matches(source))
            ranges.Add((match.Index, match.Index + match.Length, TokenType.MarkdownSyntax));

        foreach (Match match in MarkdownTaskRegex.Matches(source))
            ranges.Add((match.Index, match.Index + match.Length, TokenType.MarkdownSyntax));

        foreach (Match match in MarkdownCodeRegex.Matches(source))
        {
            var marker = match.Groups["marker"];
            var content = match.Groups["content"];
            ranges.Add((marker.Index, marker.Index + marker.Length, TokenType.MarkdownSyntax));
            ranges.Add((content.Index, content.Index + content.Length, TokenType.MarkdownCode));
            ranges.Add((content.Index + content.Length, content.Index + content.Length + marker.Length, TokenType.MarkdownSyntax));
        }

        foreach (Match match in MarkdownAlertRegex.Matches(source))
        {
            var alertType = match.Groups["kind"].Value.ToUpperInvariant() switch
            {
                "NOTE" => TokenType.MarkdownAlertNote,
                "TIP" => TokenType.MarkdownAlertTip,
                "IMPORTANT" => TokenType.MarkdownAlertImportant,
                "WARNING" => TokenType.MarkdownAlertWarning,
                "CAUTION" => TokenType.MarkdownAlertCaution,
                _ => TokenType.MarkdownSyntax
            };
            ranges.Add((match.Index, match.Index + match.Length, alertType));
        }

        return ranges;
    }

    private static List<(int Start, int End, TokenType Type)> FindFrontMatterKeyRanges(string source)
    {
        var frontMatter = MarkdownFrontMatterRegex.Match(source);
        if (!frontMatter.Success)
            return [];

        return FindYamlKeyRanges(source[..frontMatter.Length])
            .Select(range => (range.Start, range.End, TokenType.Key))
            .ToList();
    }

    private static void AddDelimitedRanges(
        string source,
        Regex regex,
        TokenType contentType,
        ICollection<(int Start, int End, TokenType Type)> ranges)
    {
        foreach (Match match in regex.Matches(source))
        {
            var marker = match.Groups["marker"];
            var content = match.Groups["content"];
            ranges.Add((marker.Index, marker.Index + marker.Length, TokenType.MarkdownSyntax));
            ranges.Add((content.Index, content.Index + content.Length, contentType));
            ranges.Add((content.Index + content.Length, content.Index + content.Length + marker.Length, TokenType.MarkdownSyntax));
        }
    }

    private IEnumerable<LanguageToken> AnalyseCore(TextReader reader)
    {
        var input = new ParserInput(reader);
        var whitespacePredicate = ParserInput.Predicate()
            .Equals(new[] { ' ', '\t' }, Repetition.OneOrMore)
            .Predicate;
        var newLinePredicate = ParserInput.Predicate()
            .Equals('\r', Repetition.ZeroOrOne)
            .Equals('\n')
            .Predicate;
        var operatorPredicate = ParserInput.Predicate()
            .EqualsAny(_operators)
            .Predicate;
        var otherTextBuilder = new StringBuilder();

        var done = false;
        while (!done)
        {
            // Whitespace
            if (input.TryReadMatch(whitespacePredicate, out var whitespace))
            {
                if (otherTextBuilder.Length != 0)
                {
                    var otherText = otherTextBuilder.ToString();
                    otherTextBuilder.Clear();
                    yield return new LanguageToken(otherText, TokenType.Text);
                }
                yield return new LanguageToken(whitespace, TokenType.Whitespace);
                continue;
            }
            
            // NewLine
            if (input.TryReadMatch(newLinePredicate, out var newLine))
            {
                if (otherTextBuilder.Length != 0)
                {
                    var otherText = otherTextBuilder.ToString();
                    otherTextBuilder.Clear();
                    yield return new LanguageToken(otherText, TokenType.Text);
                }
                yield return new LanguageToken(newLine, TokenType.NewLine);
                continue;
            }
            
            // Operator
            if (input.TryReadMatch(operatorPredicate, out var operatorString))
            {
                if (otherTextBuilder.Length != 0)
                {
                    var otherText = otherTextBuilder.ToString();
                    otherTextBuilder.Clear();
                    yield return new LanguageToken(otherText, TokenType.Text);
                }
                yield return new LanguageToken(operatorString, TokenType.Operator);
                continue;
            }
            
            // Put all other to the OtherStringBuilder
            if (input.TryReadChar(out var otherCharacter))
            {
                otherTextBuilder.Append(otherCharacter);
                continue;
            }

            if (otherTextBuilder.Length != 0)
            {
                var otherText = otherTextBuilder.ToString();
                otherTextBuilder.Clear();
                yield return new LanguageToken(otherText, TokenType.Text);
            }
            
            done = true;
        }
    }

    private static IEnumerable<LanguageToken> ApplyTokenRanges(
        IEnumerable<LanguageToken> tokens,
        int sourceLength,
        IReadOnlyCollection<(int Start, int End, TokenType Type)> ranges,
        bool onlyText = false)
    {
        var tokenTypes = new TokenType?[sourceLength];
        foreach (var (start, end, type) in ranges)
        {
            for (var index = start; index < end; index++)
                tokenTypes[index] = type;
        }

        var sourcePosition = 0;
        foreach (var token in tokens)
        {
            TokenType TypeAt(int offset) => onlyText && token.Type is not (TokenType.Text or TokenType.Operator)
                ? token.Type
                : tokenTypes[sourcePosition + offset] ?? token.Type;

            var tokenOffset = 0;
            while (tokenOffset < token.Text.Length)
            {
                var type = TypeAt(tokenOffset);
                var runStart = tokenOffset;
                while (tokenOffset < token.Text.Length &&
                       TypeAt(tokenOffset) == type)
                    tokenOffset++;

                yield return new LanguageToken(
                    token.Text[runStart..tokenOffset],
                    type);
            }

            sourcePosition += token.Text.Length;
        }
    }

    private static List<(int Start, int End)> FindYamlKeyRanges(string source)
    {
        var ranges = new List<(int Start, int End)>();
        int? blockScalarIndent = null;
        var lineStart = 0;

        while (lineStart < source.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < source.Length && source[lineEnd] is not ('\r' or '\n'))
                lineEnd++;

            var line = source[lineStart..lineEnd];
            var match = YamlMappingKeyRegex.Match(line);
            var indentation = 0;
            while (indentation < line.Length && line[indentation] is ' ' or '\t')
                indentation++;
            var isBlank = string.IsNullOrWhiteSpace(line);

            if (blockScalarIndent.HasValue)
            {
                if (isBlank || indentation > blockScalarIndent.Value)
                {
                    lineStart = NextLineStart(source, lineEnd);
                    continue;
                }

                blockScalarIndent = null;
            }

            if (match.Success)
            {
                var key = match.Groups["key"];
                var keyEnd = key.Index + key.Length;
                while (keyEnd > key.Index && line[keyEnd - 1] is ' ' or '\t')
                    keyEnd--;

                ranges.Add((lineStart + key.Index, lineStart + keyEnd));

                var valueStart = key.Index + key.Length + 1;
                while (valueStart < line.Length && line[valueStart] is ' ' or '\t')
                    valueStart++;
                if (valueStart < line.Length && line[valueStart] is '|' or '>')
                    blockScalarIndent = indentation;
            }

            lineStart = NextLineStart(source, lineEnd);
        }

        return ranges;
    }

    private static List<(int Start, int End, TokenType Type)> FindShellTokenRanges(string source)
    {
        var ranges = new List<(int Start, int End, TokenType Type)>();
        var commandExpected = true;
        var commandSeen = false;
        var subcommandSeen = false;
        var redirectionTarget = false;
        var index = 0;

        while (index < source.Length)
        {
            var character = source[index];
            if (character is '\r' or '\n')
            {
                commandExpected = true;
                commandSeen = false;
                subcommandSeen = false;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == '#')
            {
                while (index < source.Length && source[index] is not ('\r' or '\n'))
                    index++;
                continue;
            }

            if (character is ';' or '|')
            {
                while (index < source.Length && source[index] == character)
                    index++;
                if (character == ';' && index < source.Length && source[index] == '&')
                    index++;
                commandExpected = true;
                commandSeen = false;
                subcommandSeen = false;
                continue;
            }

            if (character == '&')
            {
                if (index + 1 < source.Length && source[index + 1] is '<' or '>')
                {
                    index++;
                    redirectionTarget = true;
                    continue;
                }

                while (index < source.Length && source[index] == '&')
                    index++;
                commandExpected = true;
                commandSeen = false;
                subcommandSeen = false;
                continue;
            }

            if (character is '(' or '{')
            {
                index++;
                commandExpected = true;
                commandSeen = false;
                subcommandSeen = false;
                continue;
            }

            if (character is ')' or '}')
            {
                index++;
                commandExpected = true;
                commandSeen = false;
                subcommandSeen = false;
                continue;
            }

            if (character is '<' or '>')
            {
                while (index < source.Length && source[index] is '<' or '>' or '&')
                    index++;
                redirectionTarget = true;
                continue;
            }

            var wordStart = index;
            var containsQuote = false;
            var quote = '\0';
            while (index < source.Length)
            {
                character = source[index];
                if (quote == '\0' && (char.IsWhiteSpace(character) || character is '|' or '&' or ';' or '(' or ')' or '{' or '}' or '<' or '>'))
                    break;

                if (character == '\\' && index + 1 < source.Length && quote != '\'')
                {
                    index += 2;
                    continue;
                }

                if (quote == '\0' && character is '\'' or '"')
                {
                    quote = character;
                    containsQuote = true;
                }
                else if (quote == character)
                {
                    quote = '\0';
                }

                index++;
            }

            var word = source[wordStart..index];
            if (word.Length == 0)
            {
                index++;
                continue;
            }

            if (redirectionTarget)
            {
                redirectionTarget = false;
                continue;
            }

            if (word.All(char.IsDigit) && index < source.Length && source[index] is '<' or '>')
                continue;

            if (commandExpected)
            {
                if (IsShellPrefixKeyword(word))
                    continue;

                if (IsShellKeyword(word))
                {
                    commandExpected = false;
                    continue;
                }

                if (!containsQuote && IsShellAssignment(word))
                    continue;

                if (!containsQuote)
                    ranges.Add((wordStart, index, TokenType.Command));
                commandExpected = false;
                commandSeen = true;
                subcommandSeen = false;
                continue;
            }

            if (!containsQuote && IsShellOption(word))
            {
                ranges.Add((wordStart, index, TokenType.Option));
                continue;
            }

            if (!commandSeen)
                continue;

            if (!subcommandSeen)
            {
                if (!containsQuote)
                    ranges.Add((wordStart, index, TokenType.Subcommand));
                subcommandSeen = true;
            }
            else if (!containsQuote)
            {
                ranges.Add((wordStart, index, TokenType.Argument));
            }
        }

        return ranges;
    }

    private static bool IsShellPrefixKeyword(string word) => word is
        "if" or "then" or "else" or "elif" or "while" or "until" or "do" or "time" or "!";

    private static bool IsShellKeyword(string word) => word is
        "fi" or "for" or "done" or "case" or "esac" or "in" or "function" or "select" or "coproc";

    private static bool IsShellOption(string word) =>
        word.StartsWith("--", StringComparison.Ordinal) ||
        (word.Length > 1 && word[0] == '-' && !char.IsDigit(word[1]));

    private static bool IsShellAssignment(string word)
    {
        var equalsIndex = word.IndexOf('=');
        if (equalsIndex < 1 || !(char.IsLetter(word[0]) || word[0] == '_'))
            return false;

        for (var index = 1; index < equalsIndex; index++)
        {
            if (!(char.IsLetterOrDigit(word[index]) || word[index] == '_'))
                return false;
        }

        return true;
    }

    private static int NextLineStart(string source, int lineEnd)
    {
        if (lineEnd < source.Length && source[lineEnd] == '\r')
            lineEnd++;
        if (lineEnd < source.Length && source[lineEnd] == '\n')
            lineEnd++;
        return lineEnd;
    }
}
