using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdown.Renderer;

/// <summary>One tag of the vocabulary: its name, the Markdown below its heading, and other spellings that mean it.</summary>
public sealed record TagDefinition(string Name, string DescriptionMarkdown, string Description, IReadOnlyList<string> Aliases);

/// <summary>
/// The tag vocabulary of a site, read from a Markdown page: every second-level heading names a tag, the text below it
/// describes the tag, and a paragraph starting with <c>Also:</c> lists aliases.
/// </summary>
public sealed class TagCatalog
{
    private const string AliasPrefix = "Also:";
    private static readonly MarkdownPipeline ParsePipeline = HtmlRenderer.CreatePipeline();
    private readonly Dictionary<string, TagDefinition> _byName;

    private TagCatalog(IReadOnlyList<TagDefinition> definitions, Dictionary<string, TagDefinition> byName)
    {
        Definitions = definitions;
        _byName = byName;
    }

    public IReadOnlyList<TagDefinition> Definitions { get; }

    /// <summary>Finds the tag a name or alias stands for, ignoring letter case.</summary>
    public TagDefinition? Find(string name) => _byName.GetValueOrDefault(Normalize(name));

    /// <summary>The defined tag closest to an unknown name, for "did you mean" hints; null when none is close.</summary>
    public string? Suggest(string name)
    {
        var folded = Normalize(name);
        var best = Definitions
            .SelectMany(definition => definition.Aliases.Prepend(definition.Name).Select(spelling => (definition.Name, Spelling: spelling)))
            .Select(candidate => (candidate.Name, Distance: Distance(folded, Normalize(candidate.Spelling))))
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        return best.Name is not null && best.Distance <= Math.Max(2, folded.Length / 3) ? best.Name : null;
    }

    /// <exception cref="InvalidOperationException">The page defines no tags, or a name or alias twice.</exception>
    public static TagCatalog Parse(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, ParsePipeline);
        var definitions = new List<TagDefinition>();
        var byName = new Dictionary<string, TagDefinition>(StringComparer.Ordinal);
        string? name = null;
        var description = new List<Block>();
        var aliases = new List<string>();

        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: <= 2 } heading)
            {
                Complete();
                name = heading.Level == 2 ? PlainText(heading.Inline) : null;
                continue;
            }
            if (name is null) continue;
            if (block is ParagraphBlock paragraph && IsAliasList(markdown.Substring(paragraph.Span.Start, paragraph.Span.Length), PlainText(paragraph.Inline)) is { } text)
            {
                aliases.AddRange(text[AliasPrefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }
            description.Add(block);
        }
        Complete();

        if (definitions.Count == 0)
            throw new InvalidOperationException("The tag vocabulary does not define any tags. Add a second-level heading for each tag.");
        return new TagCatalog(definitions, byName);

        void Complete()
        {
            if (name is null) return;
            if (name.Length == 0)
                throw new InvalidOperationException("A tag heading in the tag vocabulary has no text.");
            var descriptionMarkdown = string.Join("\n\n", description.Select(block => markdown.Substring(block.Span.Start, block.Span.Length)));
            var definition = new TagDefinition(name, descriptionMarkdown,
                Markdig.Markdown.ToPlainText(descriptionMarkdown, ParsePipeline).Trim(),
                // Every listed spelling stays, so that "K8S" next to "k8s" is accepted as written.
                aliases.Distinct(StringComparer.Ordinal).Where(alias => alias != name).ToArray());
            foreach (var spelling in definition.Aliases.Prepend(name))
            {
                if (byName.TryGetValue(Normalize(spelling), out var existing) && !ReferenceEquals(existing, definition))
                    throw new InvalidOperationException(
                        $"The tag vocabulary uses '{spelling}' for both '{existing.Name}' and '{name}'. Give each tag its own name and aliases.");
                byName[Normalize(spelling)] = definition;
            }
            definitions.Add(definition);
            name = null;
            description.Clear();
            aliases.Clear();
        }
    }

    /// <summary>The comparison key of a tag: case and surrounding or repeated whitespace do not matter.</summary>
    public static string Normalize(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .ToLowerInvariant();

    /// <summary>The file name of a tag page, such as <c>migration-v3</c> for "Migration v3".</summary>
    public static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (character is '+') builder.Append("plus");
            else if (character is '#') builder.Append("sharp");
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "tag" : slug;
    }

    // "Also: k8s, K8S" on a line of its own lists aliases. A sentence that happens to start with "Also:" stays text.
    private static string? IsAliasList(string source, string text) =>
        !source.Trim().Contains('\n') && text.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase) &&
        text[^1] is not ('.' or '!' or '?' or ':') ? text : null;

    private static string PlainText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var text = new StringBuilder();
        foreach (var child in inline.Descendants())
        {
            switch (child)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline: text.Append(' '); break;
            }
        }
        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }
}
