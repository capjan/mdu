using System.Text.RegularExpressions;
using Markdig.Extensions.Footnotes;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdown.Renderer;

/// <summary>
/// Removes content that authors marked as hidden from PDF output. Excluded content must be complete HTML on its own:
/// every element opened in it is closed in it, so that removing it cannot break the rest of the page.
/// </summary>
internal static class PdfHiddenContent
{
    internal const string HiddenClass = "pdf-hidden";
    internal const string StartMarker = "<!-- pdf:hidden -->";
    internal const string EndMarker = "<!-- /pdf:hidden -->";
    private static readonly Regex MarkerRegex = new(
        @"\A<!--\s*(?<end>/)?\s*pdf\s*:\s*hidden\s*-->\z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommentRegex = new(@"<!--(?<content>.*?)-->", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex MarkerLikeContentRegex = new(
        @"\A\s*/?\s*pdf[^a-z0-9>]{0,3}hid[a-z]*\s*\z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTagRegex = new(
        @"<(?<closing>/)?(?<name>[a-zA-Z][a-zA-Z0-9:-]*)(?<attributes>(?:""[^""]*""|'[^']*'|[^'"">])*)>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    // Comments, CDATA, and the content of elements that the PDF leaves out are not markup.
    private static readonly Regex InertTextRegex = new(
        @"(?<comment><!--.*?-->)|<!\[CDATA\[.*?\]\]>|<(?:script|style|template)(?=[\s/>])(?:""[^""]*""|'[^']*'|[^'"">])*/>|<(?<inert>script|style|template)(?=[\s/>])(?:""[^""]*""|'[^']*'|[^'"">])*>.*?</\k<inert>\s*>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> InertElements = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "template" };
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"
    };

    public static void Remove(MarkdownDocument document)
    {
        ValidateMarkers(document);
        var excluded = new List<SourceRange>();
        RemoveMarkedRegions(document, excluded);
        RemoveHiddenHeadingSections(document, excluded);
        RemoveUnreferencedFootnotes(document, excluded);
        EnsureVisibleLinksKeepTheirDefinitions(document, excluded);
    }

    // Markdig resolves reference links while parsing, so a visible link would still carry an excluded destination.
    private static void EnsureVisibleLinksKeepTheirDefinitions(MarkdownDocument document, List<SourceRange> excluded)
    {
        foreach (var link in document.Descendants<LinkInline>())
            if (link.Reference is { } definition && excluded.Any(range => range.Contains(definition.Span.Start)))
                throw new PdfHiddenContentException(
                    $"Link reference [{definition.Label}] is defined in excluded content but used by visible text. Move its definition outside the excluded content.");
    }

    // Markdig moves footnote definitions to the end of the document, so excluded definitions are found by position.
    private readonly record struct SourceRange(int Start, int End)
    {
        public bool Contains(int position) => position >= Start && position <= End;
    }

    /// <summary>Rejects marker-like comments that would otherwise be ignored and let excluded content into the PDF.</summary>
    private static void ValidateMarkers(MarkdownDocument document)
    {
        foreach (var block in document.Descendants<HtmlBlock>())
        {
            var text = MaskInertText(block.Lines.ToString()).Trim();
            foreach (Match comment in CommentRegex.Matches(text))
            {
                if (!MarkerLikeContentRegex.IsMatch(comment.Groups["content"].Value)) continue;
                if (!MarkerRegex.IsMatch(comment.Value))
                    throw new PdfHiddenContentException($"{comment.Value} is not a valid marker. Use {StartMarker} and {EndMarker}.");
                if (comment.Length != text.Length && GetMarker(block) != Marker.EndAfterHtml)
                    throw new PdfHiddenContentException(
                        $"{comment.Value} must stand alone on its own line, with a blank line between it and any HTML before it.");
            }
        }

        foreach (var leaf in document.Descendants<LeafBlock>())
        {
            string? inertElement = null;
            foreach (var inline in leaf.Inline?.Descendants<HtmlInline>() ?? [])
            {
                var tag = inline.Tag ?? string.Empty;
                if (tag.StartsWith("<!--", StringComparison.Ordinal))
                {
                    if (inertElement is null && CommentRegex.Matches(tag)
                            .Any(comment => MarkerLikeContentRegex.IsMatch(comment.Groups["content"].Value)))
                        throw new PdfHiddenContentException($"{tag.Trim()} must stand alone on its own line, not inside a paragraph.");
                    continue;
                }
                inertElement = NextInertElement(tag, inertElement);
            }
        }
    }

    // Keeps comments, so that markers stay visible to the validation, and blanks out everything else that is not markup.
    private static string MaskInertText(string html) =>
        InertTextRegex.Replace(html, match => match.Groups["comment"].Success ? match.Value : new string(' ', match.Length));

    private static string MaskNonMarkup(string html) => InertTextRegex.Replace(html, match => new string(' ', match.Length));

    private static string? NextInertElement(string tag, string? inertElement)
    {
        if (HtmlTagRegex.Match(tag) is not { Success: true } match) return inertElement;
        var name = match.Groups["name"].Value;
        if (inertElement is not null)
            return match.Groups["closing"].Success && name.Equals(inertElement, StringComparison.OrdinalIgnoreCase) ? null : inertElement;
        // A self-closing tag such as <script src="a.js"/> has no content, so the tags after it are markup again.
        return !match.Groups["closing"].Success && InertElements.Contains(name) && !IsSelfClosing(match) ? name : null;
    }

    private static void RemoveMarkedRegions(ContainerBlock container, List<SourceRange> excluded)
    {
        int? start = null;
        for (var index = 0; index < container.Count; index++)
        {
            var block = container[index];
            var marker = GetMarker(block);
            if (marker == Marker.Start)
            {
                if (start is not null)
                    throw new PdfHiddenContentException($"{StartMarker} opens a region before the previous one is closed by {EndMarker}.");
                start = index;
            }
            else if (marker is Marker.End or Marker.EndAfterHtml)
            {
                if (start is null)
                    throw new PdfHiddenContentException($"{EndMarker} has no preceding {StartMarker} at the same nesting level.");
                EnsureCompleteRange(container, start.Value, index);
                excluded.Add(new SourceRange(container[start.Value].Span.Start, container[index].Span.End));
                for (var remove = index; remove >= start.Value; remove--)
                    container.RemoveAt(remove);
                index = start.Value - 1;
                start = null;
            }
            else if (block is ContainerBlock child)
            {
                // Also inside an open region, so that unbalanced markers in nested containers are reported.
                RemoveMarkedRegions(child, excluded);
            }
        }

        if (start is not null)
            throw new PdfHiddenContentException($"{StartMarker} has no matching {EndMarker} at the same nesting level.");
    }

    private static void EnsureCompleteRange(ContainerBlock container, int start, int end)
    {
        var balance = new HtmlBalance();
        for (var index = start; index <= end; index++)
            foreach (var html in HtmlInDocumentOrder(container[index]))
                if (balance.Add(html) is { } problem)
                    throw IncompleteRange(problem);
        if (balance.Unclosed is { } unclosed) throw IncompleteRange(unclosed);
    }

    private static PdfHiddenContentException IncompleteRange(string problem) =>
        new($"The range closed by {EndMarker} contains incomplete HTML: {problem}. Put the range around whole HTML elements.");

    private static void RemoveHiddenHeadingSections(ContainerBlock container, List<SourceRange> excluded)
    {
        for (var index = 0; index < container.Count; index++)
        {
            var block = container[index];
            if (block is HeadingBlock heading && IsHidden(heading))
            {
                var end = FindSectionEnd(container, index + 1, heading);
                excluded.Add(new SourceRange(heading.Span.Start, LastContentEnd(container, index, end)));
                for (var remove = end - 1; remove >= index; remove--)
                    container.RemoveAt(remove);
                index--;
            }
            else if (block is ContainerBlock child)
            {
                RemoveHiddenHeadingSections(child, excluded);
            }
        }
    }

    /// <summary>
    /// Returns where the last block of a hidden section ends. Markdig moves footnote and link reference definitions out
    /// of their container, so definitions written after that block, such as the ones collected at the end of a page,
    /// stay with the page: visible text can still use them, and definitions that only excluded text uses are dropped.
    /// </summary>
    private static int LastContentEnd(ContainerBlock container, int start, int end) =>
        Enumerable.Range(start, end - start)
            .Select(index => container[index])
            .Where(block => block is not LinkReferenceDefinitionGroup)
            .Max(block => block.Span.End);

    /// <summary>
    /// A hidden section ends before the next heading of the same or a higher level in the same container, before an
    /// HTML block that starts with the closing tag of an element opened before the section, such as
    /// <c>&lt;/details&gt;</c>, or at the end of the container. Blocks generated from an OpenAPI block belong to the
    /// section as a whole.
    /// </summary>
    private static int FindSectionEnd(ContainerBlock container, int start, HeadingBlock heading)
    {
        var balance = new HtmlBalance();
        var insideOpenApi = false;
        for (var index = start; index < container.Count; index++)
        {
            var block = container[index];
            if (block is FootnoteGroup) return EndSection(index);
            if (block is HtmlBlock html)
            {
                var text = html.Lines.ToString();
                if (text.Contains(OpenApiMarkdownRenderer.BlockStartMarker, StringComparison.Ordinal)) insideOpenApi = true;
                if (text.Contains(OpenApiMarkdownRenderer.BlockEndMarker, StringComparison.Ordinal))
                {
                    insideOpenApi = false;
                    continue;
                }
                if (insideOpenApi) continue;
                if (balance.IsEmpty && MaskNonMarkup(text).TrimStart().StartsWith("</", StringComparison.Ordinal))
                    return index;
                if (balance.Add(text) is { } problem) throw IncompleteSection(heading, problem);
                continue;
            }

            if (insideOpenApi) continue;
            if (block is HeadingBlock next && next.Level <= heading.Level) return EndSection(index);
            foreach (var piece in HtmlInDocumentOrder(block))
                if (balance.Add(piece) is { } problem)
                    throw IncompleteSection(heading, problem);
        }

        return EndSection(container.Count);

        int EndSection(int index) =>
            balance.Unclosed is { } unclosed ? throw IncompleteSection(heading, $"{unclosed} before the section ends") : index;
    }

    private static PdfHiddenContentException IncompleteSection(HeadingBlock heading, string problem) =>
        new($"The section hidden by \"{HeadingText(heading)}\" contains incomplete HTML: {problem}. Exclude whole HTML elements, and put the closing tag of an element that encloses the section on its own line after a blank line.");

    /// <summary>Returns the raw HTML and inline tags of a block in document order, including nested containers.</summary>
    private static IEnumerable<string> HtmlInDocumentOrder(Block block)
    {
        switch (block)
        {
            case HtmlBlock html:
                yield return html.Lines.ToString();
                break;
            case LeafBlock { Inline: { } inline }:
                string? inertElement = null;
                foreach (var tag in inline.Descendants<HtmlInline>().Select(html => html.Tag ?? string.Empty))
                {
                    if (tag.StartsWith("<!--", StringComparison.Ordinal)) continue;
                    var wasInert = inertElement is not null;
                    inertElement = NextInertElement(tag, inertElement);
                    if (!wasInert && inertElement is null) yield return tag;
                }
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    foreach (var html in HtmlInDocumentOrder(child))
                        yield return html;
                break;
        }
    }

    /// <summary>Tracks the elements opened in excluded content, which must be closed in the same content.</summary>
    private sealed class HtmlBalance
    {
        private readonly List<string> _open = [];

        public bool IsEmpty => _open.Count == 0;

        public string? Unclosed => _open.Count == 0 ? null : $"<{_open[^1]}> is not closed";

        public string? Add(string html)
        {
            foreach (Match tag in HtmlTagRegex.Matches(MaskNonMarkup(html)))
            {
                var name = tag.Groups["name"].Value;
                if (VoidElements.Contains(name) || IsSelfClosing(tag)) continue;
                if (!tag.Groups["closing"].Success)
                {
                    _open.Add(name);
                    continue;
                }
                if (_open.Count == 0) return $"</{name}> closes an element that starts before it";
                if (!_open[^1].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return $"</{name}> does not match the open <{_open[^1]}>";
                _open.RemoveAt(_open.Count - 1);
            }

            return null;
        }
    }

    private static bool IsSelfClosing(Match tag) => tag.Groups["attributes"].Value.TrimEnd().EndsWith('/');

    private static string HeadingText(HeadingBlock heading) =>
        string.Concat(heading.Inline?.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()) ?? []).Trim();

    private static void RemoveUnreferencedFootnotes(MarkdownDocument document, List<SourceRange> excluded)
    {
        var groups = document.Descendants<FootnoteGroup>().ToArray();
        if (groups.Length == 0) return;

        var footnotes = groups.SelectMany(group => group.OfType<Footnote>()).ToArray();
        var linksInFootnotes = footnotes.ToDictionary(footnote => footnote,
            footnote => footnote.Descendants<FootnoteLink>().Where(link => !link.IsBackLink).ToArray());
        var nestedLinks = linksInFootnotes.Values.SelectMany(links => links).ToHashSet();
        var survivingLinks = document.Descendants<FootnoteLink>()
            .Where(link => !link.IsBackLink && !nestedLinks.Contains(link))
            .ToList();

        var reachable = new HashSet<Footnote>();
        var pending = new Queue<Footnote>(survivingLinks.Select(link => link.Footnote));
        while (pending.TryDequeue(out var footnote))
        {
            if (!reachable.Add(footnote) || !linksInFootnotes.TryGetValue(footnote, out var links)) continue;
            survivingLinks.AddRange(links);
            foreach (var link in links) pending.Enqueue(link.Footnote);
        }

        foreach (var footnote in reachable)
            if (excluded.Any(range => range.Contains(footnote.Span.Start)))
                throw new PdfHiddenContentException(
                    $"Footnote [{footnote.Label}] is defined in excluded content but referenced from visible text. Move its definition outside the excluded content.");

        var survivingIndexes = survivingLinks.Select(link => link.Index).ToHashSet();
        var order = 1;
        foreach (var group in groups)
        {
            for (var index = group.Count - 1; index >= 0; index--)
                if (group[index] is Footnote footnote && !reachable.Contains(footnote))
                    group.RemoveAt(index);
            foreach (var footnote in group.OfType<Footnote>())
            {
                footnote.Order = order++;
                RemoveDeadBackLinks(footnote, survivingIndexes);
            }
            if (group.Count == 0) group.Parent?.Remove(group);
        }
    }

    private static void RemoveDeadBackLinks(Footnote footnote, HashSet<int> survivingIndexes)
    {
        foreach (var backLink in footnote.Descendants<FootnoteLink>()
                     .Where(link => link.IsBackLink && !survivingIndexes.Contains(link.Index)).ToArray())
            backLink.Remove();
    }

    private static Marker GetMarker(Block block)
    {
        if (block is not HtmlBlock html) return Marker.None;
        var match = MarkerRegex.Match(html.Lines.ToString().Trim());
        if (match.Success) return match.Groups["end"].Success ? Marker.End : Marker.Start;

        // Without a blank line, an end marker after HTML such as </details> becomes the last line of that HTML block.
        var lines = html.Lines.Lines.Take(html.Lines.Count).Select(line => line.Slice.ToString().Trim())
            .Where(line => line.Length > 0).ToArray();
        return lines.Length > 1 && MarkerRegex.Match(lines[^1]) is { Success: true } last && last.Groups["end"].Success &&
            !CommentRegex.Matches(MaskInertText(string.Join('\n', lines[..^1])))
                .Any(comment => MarkerLikeContentRegex.IsMatch(comment.Groups["content"].Value))
            ? Marker.EndAfterHtml
            : Marker.None;
    }

    private static bool IsHidden(HeadingBlock heading) =>
        heading.TryGetAttributes()?.Classes?.Contains(HiddenClass) == true;

    private enum Marker
    {
        None,
        Start,
        End,
        EndAfterHtml
    }
}
