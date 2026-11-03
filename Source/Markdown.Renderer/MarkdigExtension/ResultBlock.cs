using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Markdown.Renderer.MarkdigExtension;

/// <summary>The outcome of the code block above it, written as a block quote that starts with <c>[!RESULT]</c>.</summary>
public sealed class ResultBlock() : ContainerBlock(null)
{
    public const string Title = "Result";
}

internal sealed class ResultBlockRenderer : HtmlObjectRenderer<ResultBlock>
{
    protected override void Write(Markdig.Renderers.HtmlRenderer renderer, ResultBlock obj)
    {
        renderer.EnsureLine();
        if (renderer.EnableHtmlForBlock)
            renderer.WriteLine("<div class=\"result-block\">")
                .Write("<p class=\"result-block-title\">").Write(ResultBlock.Title).WriteLine("</p>");
        renderer.WriteChildren(obj);
        if (renderer.EnableHtmlForBlock) renderer.WriteLine("</div>");
        renderer.EnsureLine();
    }
}
