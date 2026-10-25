using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdown.Renderer.MarkdigExtension;

/// <summary>
/// Keeps leading digits in heading ids, as GitHub and MkDocs do: <c>## 6. Security</c> gets <c>6-security</c>.
/// Markdig's auto identifiers drop everything before the first letter, so this runs first and assigns the id
/// for headings that start with a digit; Markdig then keeps it. Explicit ids such as <c>{#custom}</c> stay unchanged.
/// </summary>
public class HeadingIdExtension : IMarkdownExtension
{
    private static readonly object IdentifiersKey = new();

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Setext headings ("Title" underlined with "---") are closed by the paragraph parser, as in Markdig.
        BlockParser?[] parsers = [pipeline.BlockParsers.Find<HeadingBlockParser>(), pipeline.BlockParsers.FindExact<ParagraphBlockParser>()];
        foreach (var parser in parsers.OfType<BlockParser>())
        {
            parser.Closed -= HeadingBlockParserClosed;
            parser.Closed += HeadingBlockParserClosed;
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void HeadingBlockParserClosed(BlockProcessor processor, Block block)
    {
        if (block is not HeadingBlock headingBlock) return;
        headingBlock.ProcessInlinesEnd += HeadingBlockProcessInlinesEnd;
    }

    private static void HeadingBlockProcessInlinesEnd(InlineProcessor processor, Inline? inline)
    {
        var headingBlock = (HeadingBlock)processor.Block!;
        if (headingBlock.Inline is null) return;
        if (processor.Document.GetData(IdentifiersKey) is not HashSet<string> identifiers)
        {
            identifiers = new HashSet<string>(StringComparer.Ordinal);
            processor.Document.SetData(IdentifiersKey, identifiers);
        }

        var attributes = headingBlock.GetAttributes();
        if (attributes.Id is not null)
        {
            // An explicit id such as {#6-security} must not be handed out again to a later heading.
            identifiers.Add(attributes.Id);
            return;
        }

        var baseId = GetIdWithLeadingNumber(GetPlainText(headingBlock.Inline));
        // Headings that start with a letter keep Markdig's id, which also handles duplicates among them.
        if (baseId is null) return;

        // Markdig's ids never start with a digit, so only explicit ids and ids assigned here can collide.
        var id = baseId;
        for (var index = 1; !identifiers.Add(id); index++)
            id = $"{baseId}-{index}";
        attributes.Id = id;
    }

    /// <summary>
    /// Returns the id for a heading that starts with a number, or <c>null</c> otherwise. The number before the first
    /// letter loses its dots, as in MkDocs (<c>3.1 Overview</c> gets <c>31-overview</c>, <c>2FA Setup</c> gets <c>2fa-setup</c>).
    /// </summary>
    internal static string? GetIdWithLeadingNumber(string headingText)
    {
        var letterIndex = 0;
        while (letterIndex < headingText.Length && !char.IsLetter(headingText[letterIndex])) letterIndex++;
        var leadingText = headingText[..letterIndex];
        if (!leadingText.Any(char.IsAsciiDigit)) return null;

        var id = LinkHelper.Urilize(leadingText.Replace(".", string.Empty) + headingText[letterIndex..],
            allowOnlyAscii: true, keepOpeningDigits: true);
        return id.Length > 0 && char.IsAsciiDigit(id[0]) ? id : null;
    }

    private static string GetPlainText(ContainerInline inline)
    {
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer)
        {
            EnableHtmlForInline = false,
            EnableHtmlEscape = false
        };
        renderer.Render(inline);
        writer.Flush();
        return writer.ToString();
    }
}

public static class HeadingIdPipelineExtensions
{
    /// <summary>
    /// Keeps leading digits in heading ids. Call after <c>UseAdvancedExtensions</c>, which adds Markdig's auto identifiers.
    /// </summary>
    public static MarkdownPipelineBuilder UseHeadingIds(this MarkdownPipelineBuilder pipeline)
    {
        if (pipeline.Extensions.Contains<HeadingIdExtension>()) return pipeline;
        if (pipeline.Extensions.Contains<AutoIdentifierExtension>())
            pipeline.Extensions.InsertBefore<AutoIdentifierExtension>(new HeadingIdExtension());
        else
            pipeline.Extensions.Add(new HeadingIdExtension());
        return pipeline;
    }
}
