using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StaticCode.Highlighter;

/// <summary>
/// Finds code annotation markers in a token stream, in the syntax of Material for MkDocs. A marker <c>(1)</c> counts
/// anywhere in a comment, and <c>(1)!</c> there also removes the comment characters of an otherwise empty comment.
/// Outside comments only the explicit form <c>(1)!</c> counts, at the end of a line after a space, and only where the
/// language allows it (see <see cref="Lexer.AllowsLineEndMarkers"/>). So code and output such as <c>print(1)</c>,
/// SQL's <c>VALUES (1)</c>, or a log line <c>Retrying (1)</c> stay as they are.
/// </summary>
public static class CodeAnnotationMarkers
{
    private static readonly Regex CommentMarker = new(
        @"\((?<number>[1-9][0-9]{0,2})\)(?<strip>!?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // The space before the marker goes with it, so the button follows the code directly.
    private static readonly Regex LineEndMarker = new(
        @"[ \t]+\((?<number>[1-9][0-9]{0,2})\)![ \t]*(?=\r?\n|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string CommentCharacters = " \t#/*-<>!;%'";

    /// <summary>Returns the annotation numbers that the tokens contain, in order of appearance and without duplicates.</summary>
    /// <param name="tokens">The analysed code.</param>
    /// <param name="lineEndMarkers">Whether markers also count outside comments at the end of a line.</param>
    public static IReadOnlyList<int> FindNumbers(IEnumerable<LanguageToken> tokens, bool lineEndMarkers) =>
        Find(tokens as IReadOnlyList<LanguageToken> ?? tokens.ToList(), _ => true, lineEndMarkers)
            .Where(marker => marker.Number > 0)
            .Select(marker => marker.Number)
            .Distinct()
            .ToList();

    /// <summary>
    /// Replaces each marker whose number <paramref name="isAnnotation"/> accepts with an <see cref="TokenType.Annotation"/>
    /// token. Other markers stay code.
    /// </summary>
    /// <param name="tokens">The analysed code.</param>
    /// <param name="isAnnotation">Whether a number has an entry.</param>
    /// <param name="lineEndMarkers">Whether markers also count outside comments at the end of a line.</param>
    public static IReadOnlyList<LanguageToken> Apply(IEnumerable<LanguageToken> tokens, Func<int, bool> isAnnotation, bool lineEndMarkers)
    {
        var list = tokens as IReadOnlyList<LanguageToken> ?? tokens.ToList();
        var markers = Find(list, isAnnotation, lineEndMarkers);
        if (markers.Count == 0) return list;

        var result = new List<LanguageToken>(list.Count + markers.Count * 2);
        var markerIndex = 0;
        var tokenStart = 0;
        foreach (var token in list)
        {
            var tokenEnd = tokenStart + token.Text.Length;
            var cursor = tokenStart;
            while (markerIndex < markers.Count && markers[markerIndex].Start < tokenEnd)
            {
                var marker = markers[markerIndex];
                if (marker.Start > cursor) result.Add(new LanguageToken(token.Text[(cursor - tokenStart)..(marker.Start - tokenStart)], token.Type));
                if (marker.Start >= tokenStart && marker.Number > 0)
                    result.Add(new LanguageToken(marker.Number.ToString(CultureInfo.InvariantCulture), TokenType.Annotation));
                cursor = Math.Max(cursor, Math.Min(marker.End, tokenEnd));
                if (marker.End > tokenEnd) break;
                markerIndex++;
            }
            if (cursor < tokenEnd) result.Add(new LanguageToken(token.Text[(cursor - tokenStart)..], token.Type));
            tokenStart = tokenEnd;
        }

        return result;
    }

    /// <summary>
    /// Returns the code as it is copied: without markers, without comments that held nothing but markers, and without the
    /// spaces that a removed marker leaves at the end of a line. Other spaces stay, such as those that end a line of
    /// Markdown or separate the code around a marker in the middle of a line.
    /// </summary>
    public static string CopyText(IEnumerable<LanguageToken> tokens)
    {
        var list = tokens as IReadOnlyList<LanguageToken> ?? tokens.ToList();
        var text = new StringBuilder();
        var afterMarker = false;
        for (var index = 0; index < list.Count; index++)
        {
            var token = list[index];
            if (token.Type == TokenType.Annotation || IsMarkerOnlyComment(list, index))
            {
                while (text.Length > 0 && text[^1] is ' ' or '\t') text.Length--;
                afterMarker = true;
                continue;
            }
            if (afterMarker && token.Type == TokenType.Whitespace && EndsLine(list, index)) continue;
            afterMarker = false;
            text.Append(token.Text);
        }
        return text.ToString();
    }

    // Whether only whitespace follows the token on its line.
    private static bool EndsLine(IReadOnlyList<LanguageToken> tokens, int index)
    {
        for (var next = index + 1; next < tokens.Count; next++)
        {
            if (tokens[next].Type == TokenType.Whitespace) continue;
            return tokens[next].Type == TokenType.NewLine;
        }
        return true;
    }

    // A comment whose text is only comment characters and that a marker follows, such as "# " before "(2)".
    private static bool IsMarkerOnlyComment(IReadOnlyList<LanguageToken> tokens, int index)
    {
        if (tokens[index].Type != TokenType.Comment || tokens[index].Text.Trim(CommentCharacters.ToCharArray()).Length > 0) return false;
        for (var next = index + 1; next < tokens.Count; next++)
        {
            if (tokens[next].Type == TokenType.Whitespace) continue;
            return tokens[next].Type == TokenType.Annotation;
        }
        return false;
    }

    // A marker removes Start..End from the code and, unless its number is 0, puts an annotation token at Start. Attach
    // tells whether it moves to the word before it; a marker right after visible comment characters keeps its space.
    private readonly record struct Marker(int Start, int End, int Number, bool Attach = true);

    private static List<Marker> Find(IReadOnlyList<LanguageToken> tokens, Func<int, bool> isAnnotation, bool lineEndMarkers)
    {
        var markers = new List<Marker>();
        var comments = new List<(int Start, int End)>();
        var source = new StringBuilder();
        foreach (var token in tokens)
        {
            var start = source.Length;
            source.Append(token.Text);
            if (token.Type != TokenType.Comment) continue;
            comments.Add((start, source.Length));
            AddCommentMarkers(token.Text, start, isAnnotation, markers);
        }

        IEnumerable<Match> lineEnds = lineEndMarkers ? LineEndMarker.Matches(source.ToString()) : [];
        foreach (var match in lineEnds)
        {
            var number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
            if (!isAnnotation(number)) continue;
            var markerStart = match.Groups["number"].Index - 1;
            if (comments.Any(comment => markerStart >= comment.Start && markerStart < comment.End)) continue;
            markers.Add(new Marker(match.Index, match.Index + match.Length, number));
        }

        markers.Sort((left, right) => left.Start.CompareTo(right.Start));
        return AttachToPrecedingText(markers, source);
    }

    // Like a footnote reference, a marker sits directly after the word before it: the spaces between them go, unless
    // nothing but indentation precedes the marker on its line.
    private static List<Marker> AttachToPrecedingText(List<Marker> markers, StringBuilder source)
    {
        var previousEnd = 0;
        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];
            var start = marker.Start;
            while (start > previousEnd && source[start - 1] is ' ' or '\t') start--;
            if (marker.Number > 0 && marker.Attach && start > 0 && source[start - 1] is not ('\n' or '\r'))
                markers[index] = marker with { Start = start };
            previousEnd = markers[index].End;
        }
        return markers;
    }

    private static void AddCommentMarkers(string comment, int offset, Func<int, bool> isAnnotation, List<Marker> markers)
    {
        var matches = CommentMarker.Matches(comment)
            .Where(match => isAnnotation(int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)))
            .ToList();
        if (matches.Count == 0) return;

        var removesComment = matches.Any(match => match.Groups["strip"].Length > 0) && IsOnlyMarkers(comment, matches);
        var previousEnd = 0;
        foreach (var match in matches)
        {
            var number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
            var start = removesComment ? previousEnd : match.Index;
            // "# (1)" keeps "# " visible, and the marker reads better after a space than glued to "#".
            var attach = removesComment || comment[..match.Index].Trim(CommentCharacters.ToCharArray()).Length > 0;
            markers.Add(new Marker(offset + start, offset + match.Index + match.Length, number, attach));
            previousEnd = match.Index + match.Length;
        }
        if (removesComment && previousEnd < comment.Length)
            markers.Add(new Marker(offset + previousEnd, offset + comment.Length, 0));
    }

    private static bool IsOnlyMarkers(string comment, IEnumerable<Match> matches)
    {
        var rest = new StringBuilder(comment);
        foreach (var match in matches.OrderByDescending(match => match.Index)) rest.Remove(match.Index, match.Length);
        return rest.ToString().Trim(CommentCharacters.ToCharArray()).Length == 0;
    }
}
