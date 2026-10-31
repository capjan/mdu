using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Renderer;

/// <summary>The validated abbreviation definitions of a glossary file.</summary>
public sealed class Glossary
{
    private static readonly MarkdownPipeline ParsePipeline = HtmlRenderer.CreatePipeline();

    private Glossary(SharedAbbreviation[] definitions) => Definitions = definitions;

    internal SharedAbbreviation[] Definitions { get; }

    /// <exception cref="InvalidOperationException">The Markdown does not contain any abbreviation definitions.</exception>
    public static Glossary Parse(string markdown)
    {
        var definitions = Markdig.Markdown.Parse(markdown, ParsePipeline).GetAbbreviations()?
            .Select(pair => new SharedAbbreviation(pair.Key, pair.Value.Label ?? pair.Key, pair.Value.Text.ToString()))
            .ToArray() ?? [];
        if (definitions.Length == 0)
            throw new InvalidOperationException("The configured glossary does not contain any abbreviation definitions.");
        return new Glossary(definitions);
    }
}
