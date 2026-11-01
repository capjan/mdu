using Markdig;

namespace Markdown.Renderer;

/// <summary>The abbreviations that apply to the pages of one glossary scope, such as an imported repository.</summary>
public sealed class GlossaryScope
{
    internal GlossaryScope(MarkdownPipeline pipeline) => Pipeline = pipeline;

    internal MarkdownPipeline Pipeline { get; }
}
