using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdown.Renderer.MarkdigExtension;

/// <summary>
/// Restricts Markdig's alert blocks to GitHub's behavior: only NOTE, TIP, IMPORTANT, WARNING, and CAUTION
/// alerts with content are rendered as alerts; other block quotes keep their marker text. Titles are rendered without icons.
/// </summary>
public class GitHubAlertExtension : IMarkdownExtension
{
    private static readonly string[] AlertKinds = { "Note", "Tip", "Important", "Warning", "Caution" };

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= ApplyGitHubRules;
        pipeline.DocumentProcessed += ApplyGitHubRules;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not Markdig.Renderers.HtmlRenderer htmlRenderer) return;
        var alertRenderer = htmlRenderer.ObjectRenderers.FindExact<AlertBlockRenderer>();
        if (alertRenderer != null) alertRenderer.RenderKind = RenderTitle;
    }

    private static void RenderTitle(Markdig.Renderers.HtmlRenderer renderer, StringSlice kind) =>
        renderer.Write("<p class=\"markdown-alert-title\">").Write(GetTitle(kind.ToString())).WriteLine("</p>");

    internal static string? GetTitle(string kind) =>
        AlertKinds.FirstOrDefault(title => title.Equals(kind, StringComparison.OrdinalIgnoreCase));

    private static void ApplyGitHubRules(MarkdownDocument document)
    {
        for (var index = 0; index < document.Count; index++)
        {
            if (document[index] is not AlertBlock alert) continue;

            var markerParagraph = alert.FirstOrDefault() as ParagraphBlock;
            var markerParagraphIsEmpty = markerParagraph is { Inline.FirstChild: null } or { Inline: null };
            var hasContent = alert.Count > (markerParagraphIsEmpty ? 1 : 0);

            if (GetTitle(alert.Kind.ToString()) != null && hasContent)
            {
                if (markerParagraphIsEmpty) alert.Remove(markerParagraph!);
                continue;
            }

            document[index] = ToQuoteBlock(alert, markerParagraph);
        }
    }

    private static QuoteBlock ToQuoteBlock(AlertBlock alert, ParagraphBlock? markerParagraph)
    {
        if (markerParagraph != null)
        {
            markerParagraph.Inline ??= new ContainerInline();
            var firstInline = markerParagraph.Inline.FirstChild;
            var marker = new LiteralInline($"[!{alert.Kind}]");
            if (firstInline == null)
            {
                markerParagraph.Inline.AppendChild(marker);
            }
            else
            {
                firstInline.InsertBefore(marker);
                firstInline.InsertBefore(new LineBreakInline());
            }
        }

        var quote = new QuoteBlock(null)
        {
            QuoteChar = alert.QuoteChar,
            Span = alert.Span,
            Line = alert.Line,
            Column = alert.Column
        };
        while (alert.Count > 0)
        {
            var child = alert[0];
            alert.RemoveAt(0);
            quote.Add(child);
        }

        return quote;
    }
}
