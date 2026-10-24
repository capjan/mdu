using System.Text.RegularExpressions;
using Core.Extensions.ParserRelated;
using Core.Parser;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Document.Model;
using Markdown.Document.Model.Impl;

namespace Markdown.Document.Impl;

public class MarkdownDocumentReader : IMarkdownDocumentReader
{
    private static readonly MarkdownPipeline LinkParsingPipeline =
        new MarkdownPipelineBuilder().UseFootnotes().UsePreciseSourceLocation().Build();
    private static readonly Regex MermaidFlowchartHeaderRegex = new(
        @"^(?:flowchart|graph)(?:[ \t]+(?:TB|TD|BT|LR|RL))?[ \t]*(?:;.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MermaidNodeLinkRegex = new(
        "^click[ \\t]+(?<id>[\\p{L}\\p{N}_]+)[ \\t]+(?:href[ \\t]+)?(?:\"(?<doubleUrl>[^\"]*)\"|'(?<singleUrl>[^']*)'|(?<bareUrl>[^ \\t]+))(?:[ \\t]+\"[^\"]*\")?[ \\t]*;?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlantUmlNodeLinkRegex = new(
        "^url[ \\t]+of[ \\t]+(?<id>[\\p{L}\\p{N}_.]+)[ \\t]+is[ \\t]+\\[\\[(?<url>[^\\]]+)\\]\\][ \\t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IMarkdownHeader ReadHeader(IParserInput input)
    {
        var content = input.ReadAll();
        var m = Regex.Match(content, RegExPattern.MarkdownHeader);

        var frontMatter = string.Empty;
        var title = string.Empty;

        if (!m.Success) return new MarkdownHeader(title, frontMatter);
        title = m.Groups["titleName"].Value;
        frontMatter = m.Groups["frontMatter"].Value;

        return new MarkdownHeader(title, frontMatter);
    }

    public IEnumerable<IMarkdownLink> ReadLinks(IParserInput input) => ReadAnalysis(input).Links;

    public MarkdownDocumentAnalysis ReadAnalysis(IParserInput input)
    {
        var content = input.ReadAll();
        var document = Markdig.Parsers.MarkdownParser.Parse(content, LinkParsingPipeline);
        var definitions = Markdig.Syntax.LinkReferenceDefinitionExtensions
            .GetLinkReferenceDefinitions(document, addGroup: false);
        var footnoteDefinitions = definitions?.OfType<FootnoteLinkReferenceDefinition>().ToArray() ?? [];
        var markdownLinks = document.Descendants<LinkInline>()
            .Concat(footnoteDefinitions.SelectMany(definition => definition.Footnote.Descendants<LinkInline>()))
            .Distinct()
            .Select(link =>
            {
                var url = link.Url ?? "";
                var literals = link.Descendants<LiteralInline>().ToArray();
                var text = literals.Length == 1 ? literals[0].ToString() : "";
                var isImage = link.IsImage;
                var lineNumber = link.Line + 1;
                return new MarkdownLink(text, url, isImage, lineNumber);
            });
        var diagramLinks = document.Descendants<FencedCodeBlock>().SelectMany(ReadDiagramLinks);
        var footnotes = ReadFootnotes(document, definitions);

        return new MarkdownDocumentAnalysis(
            markdownLinks.Concat(diagramLinks).ToArray(),
            footnotes.UnusedFootnotes,
            footnotes.DuplicateFootnotes);
    }

    private static (IReadOnlyList<MarkdownFootnote> UnusedFootnotes, IReadOnlyList<MarkdownFootnote> DuplicateFootnotes)
        ReadFootnotes(
            Markdig.Syntax.MarkdownDocument document,
            LinkReferenceDefinitionGroup? definitions)
    {
        if (definitions is null) return ([], []);

        var definitionNodes = definitions.OfType<FootnoteLinkReferenceDefinition>().ToArray();
        var duplicateFootnotes = definitionNodes
            .GroupBy(definition => definition.Footnote.Label!, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Skip(1).Select(definition =>
                new MarkdownFootnote(definition.Footnote.Label!, definition.Line + 1)))
            .ToArray();

        var footnotes = definitions.Links.Values
            .OfType<FootnoteLinkReferenceDefinition>()
            .Select(definition => definition.Footnote)
            .ToArray();
        var reachableFootnotes = FindReachableFootnotes(document,
            definitionNodes.Select(definition => definition.Footnote).ToArray());
        var unusedFootnotes = footnotes
            .Where(footnote => !reachableFootnotes.Contains(footnote))
            .Select(footnote => new MarkdownFootnote(footnote.Label!, footnote.Line + 1))
            .ToArray();

        return (unusedFootnotes, duplicateFootnotes);
    }

    private static HashSet<Footnote> FindReachableFootnotes(
        Markdig.Syntax.MarkdownDocument document,
        IReadOnlyCollection<Footnote> footnotes)
    {
        var reachable = new HashSet<Footnote>();
        var referencesInsideFootnotes = footnotes
            .SelectMany(footnote => footnote.Descendants<FootnoteLink>())
            .ToHashSet();
        var pending = new Stack<Footnote>(document.Descendants<FootnoteLink>()
            .Where(reference => !reference.IsBackLink && !referencesInsideFootnotes.Contains(reference))
            .Select(reference => reference.Footnote));

        while (pending.TryPop(out var footnote))
        {
            if (!reachable.Add(footnote)) continue;
            foreach (var reference in footnote.Descendants<FootnoteLink>())
            {
                if (!reference.IsBackLink) pending.Push(reference.Footnote);
            }
        }

        return reachable;
    }

    private static IEnumerable<IMarkdownLink> ReadDiagramLinks(FencedCodeBlock block)
    {
        var language = block.Info?.Trim().Split([' ', '\t'], 2)[0]?.ToLowerInvariant();
        if (language == "mermaid" && !IsFlowchart(block)) yield break;

        var linkRegex = language switch
        {
            "mermaid" or "context" => MermaidNodeLinkRegex,
            "plantuml" or "puml" => PlantUmlNodeLinkRegex,
            _ => null
        };

        if (linkRegex == null || block.Lines.Lines == null) yield break;

        for (var index = 0; index < block.Lines.Lines.Length; index++)
        {
            var line = block.Lines.Lines[index].Slice.ToString().Trim();
            var match = linkRegex.Match(line);
            if (!match.Success) continue;

            var destination = match.Groups["url"].Success ? match.Groups["url"].Value
                : match.Groups["doubleUrl"].Success ? match.Groups["doubleUrl"].Value
                : match.Groups["singleUrl"].Success ? match.Groups["singleUrl"].Value
                : match.Groups["bareUrl"].Value;
            yield return new MarkdownLink(match.Groups["id"].Value, destination, false, block.Line + index + 2);
        }
    }

    private static bool IsFlowchart(FencedCodeBlock block)
    {
        if (block.Lines.Lines == null) return false;

        foreach (var line in block.Lines.Lines)
        {
            var text = line.Slice.ToString().Trim();
            if (text.Length == 0 || text.StartsWith("%%", StringComparison.Ordinal)) continue;
            return MermaidFlowchartHeaderRegex.IsMatch(text);
        }

        return false;
    }

    private bool TryParseLink(IParserInput input, out IMarkdownLink link)
    {
        link = Contants.EmptyLink;
        return false;
    }

}
