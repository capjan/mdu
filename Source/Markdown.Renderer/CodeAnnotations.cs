using System.Globalization;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Highlighter;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>
/// Pairs code blocks with the numbered list directly after them, whose entries explain the markers <c>(1)</c>, <c>(2)</c>,
/// … in the code, as in Material for MkDocs. Annotations are on by default. <c>content.code.annotate</c> in
/// <c>mdu.yml</c> or in a page's front matter and <c>annotate=</c> after a code block's language switch them, and the
/// narrowest of these settings wins.
/// </summary>
public static class CodeAnnotations
{
    public const string Setting = "content.code.annotate";
    private static readonly object DataKey = typeof(CodeAnnotations);
    private static readonly Regex InfoSetting = new(@"(?:^|\s)annotate=(?<value>\S*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A pair of a code block and its annotation list, with the code's tokens and the numbers of the markers that the code
    /// contains. The renderer reuses the tokens, so the code is analysed once.
    /// </summary>
    public sealed record Annotations(FencedCodeBlock Code, ListBlock List, IReadOnlyList<LanguageToken> Tokens, IReadOnlyList<int> Markers)
    {
        public int Count => List.Count;
    }

    /// <summary>
    /// Moves every annotation list into its code block, so that the list is rendered with the code instead of after it.
    /// Run it after anything that removes blocks from the page, such as hidden PDF content, so it sees the final page.
    /// </summary>
    /// <param name="document">The parsed page.</param>
    /// <param name="enabled">The page's setting: its front matter, or else the site's.</param>
    public static void Apply(MarkdownDocument document, bool enabled)
    {
        foreach (var annotations in Find(document, enabled))
        {
            annotations.List.Parent?.Remove(annotations.List);
            annotations.Code.SetData(DataKey, annotations);
        }
    }

    /// <summary>Returns the code blocks that have annotations, without changing the document.</summary>
    public static IReadOnlyList<Annotations> Find(MarkdownDocument document, bool enabled)
    {
        var found = new List<Annotations>();
        foreach (var container in document.Descendants<ContainerBlock>().Prepend(document).ToList())
            for (var index = 0; index < container.Count; index++)
                if (TryPair(container, index, enabled) is { } annotations) found.Add(annotations);
        return found;
    }

    /// <summary>
    /// Returns the code of a block as the search index stores it: without the markers of its annotations, which readers
    /// see as buttons. Blocks without annotations keep their code.
    /// </summary>
    public static string SearchText(FencedCodeBlock code)
    {
        if (code.Parent is not { } parent || TryPair(parent, parent.IndexOf(code), enabled: true) is not { } annotations)
            return HighlightedCodeBlockRenderer.GetCode(code);
        return CodeAnnotationMarkers.CopyText(CodeAnnotationMarkers.Apply(annotations.Tokens, number => number <= annotations.Count));
    }

    private static Annotations? TryPair(ContainerBlock container, int index, bool enabled)
    {
        if (index < 0 || container[index] is not FencedCodeBlock code) return null;
        var next = index + 1;
        if (next < container.Count && container[next] is ResultBlock) next++;
        if (next >= container.Count || container[next] is not ListBlock { IsOrdered: true } list) return null;
        if (list.OrderedStart is { Length: > 0 } start && start != "1") return null;
        if (!(ReadInfo(code) ?? enabled)) return null;

        var language = HighlightedCodeBlockRenderer.GetLanguage(code);
        if (!HighlightedCodeBlockRenderer.IsCodeLanguage(language)) return null;
        var tokens = Lexer.ForLanguage(language).Analyse(HighlightedCodeBlockRenderer.GetCode(code)).ToList();
        var markers = CodeAnnotationMarkers.FindNumbers(tokens);
        return markers.Any(number => number <= list.Count) ? new Annotations(code, list, tokens, markers) : null;
    }

    /// <summary>A problem with the annotations of a page, at a 1-based line.</summary>
    public sealed record Problem(int Line, ProblemKind Kind, string Message);

    public enum ProblemKind
    {
        /// <summary>A marker in the code has no entry in the list.</summary>
        MarkerWithoutEntry,
        /// <summary>An entry of the list has no marker in the code.</summary>
        EntryWithoutMarker,
        /// <summary>An <c>annotate=</c> value that is neither true nor false.</summary>
        InvalidSetting
    }

    private static readonly Lazy<Markdig.MarkdownPipeline> Pipeline = new(() => HtmlRenderer.CreatePipeline());

    /// <summary>Checks the annotations of a page as <c>mdu render</c> would pair them.</summary>
    /// <param name="markdown">The page.</param>
    /// <param name="enabled">The page's setting: its front matter, or else the site's.</param>
    public static IReadOnlyList<Problem> FindProblems(string markdown, bool enabled)
    {
        if (!markdown.Contains('(') && !markdown.Contains("annotate=", StringComparison.Ordinal)) return [];
        var document = Markdig.Markdown.Parse(markdown, Pipeline.Value);
        var problems = new List<Problem>();
        foreach (var code in document.Descendants<FencedCodeBlock>())
        {
            var match = InfoSetting.Match(code.Arguments ?? string.Empty);
            if (match.Success && !bool.TryParse(match.Groups["value"].Value, out _))
                problems.Add(new Problem(code.Line + 1, ProblemKind.InvalidSetting,
                    $"annotate= after the code block's language must be 'true' or 'false', not '{match.Groups["value"].Value}'."));
        }

        foreach (var annotations in Find(document, enabled))
        {
            foreach (var number in annotations.Markers.Where(number => number > annotations.Count))
                problems.Add(new Problem(annotations.Code.Line + 1, ProblemKind.MarkerWithoutEntry,
                    $"Code annotation ({number}) has no entry {number} in the numbered list after the code block."));
            var entry = 0;
            foreach (var item in annotations.List)
            {
                entry++;
                if (annotations.Markers.Contains(entry)) continue;
                problems.Add(new Problem(item.Line + 1, ProblemKind.EntryWithoutMarker,
                    $"Entry {entry} of the list after the code block has no marker ({entry}) in the code. Add one, or write 'annotate=false' after the language if the list does not explain the code."));
            }
        }
        return problems.OrderBy(problem => problem.Line).ToList();
    }

    internal static Annotations? GetAnnotations(CodeBlock code) => code.GetData(DataKey) as Annotations;

    /// <summary>Ids of a block's annotations: its position in the source tells blocks on one page apart, as for diagrams.</summary>
    internal static string IdPrefix(CodeBlock code) =>
        $"code-annotation-{code.Span.Start.ToString(CultureInfo.InvariantCulture)}-";

    /// <summary>Returns the <c>annotate=</c> setting after the code block's language, or null when it has none.</summary>
    public static bool? ReadInfo(FencedCodeBlock code)
    {
        var match = InfoSetting.Match(code.Arguments ?? string.Empty);
        return match.Success && bool.TryParse(match.Groups["value"].Value, out var value) ? value : null;
    }

    /// <summary>Returns the page's <c>content.code.annotate</c> setting, nested or dotted, or null when it has none.</summary>
    public static bool? ReadFrontMatter(string? frontMatter) =>
        FindFrontMatterSetting(frontMatter) is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var value) ? value : null;

    /// <summary>Describes what is wrong with a page's <c>content.code.annotate</c> setting, or returns null when it is valid or absent.</summary>
    public static string? FindProblem(string? frontMatter) => FindFrontMatterSetting(frontMatter) switch
    {
        null => null,
        YamlScalarNode scalar when bool.TryParse(scalar.Value, out _) => null,
        YamlScalarNode scalar => InvalidValue(scalar.Value ?? string.Empty),
        var node => InvalidValue(node.ToString())
    };

    private static string InvalidValue(string value) => $"{Setting} must be 'true' or 'false', not '{value}'.";

    private static YamlNode? FindFrontMatterSetting(string? frontMatter)
    {
        if (frontMatter is null || !frontMatter.Contains("annotate", StringComparison.Ordinal)) return null;
        return FrontMatterYaml.ReadMapping(frontMatter) is { } metadata
            ? FrontMatterYaml.FindPath(metadata, Setting.Split('.'))
            : null;
    }
}
