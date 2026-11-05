using Markdig;
using Markdig.Extensions.Alerts;

namespace Markdown.Renderer.MarkdigExtension;

public static class MarkdownPipelineRelated
{
    public static MarkdownPipelineBuilder UseSharedAbbreviations(this MarkdownPipelineBuilder pipeline, string markdown)
    {
        pipeline.Extensions.Add(SharedAbbreviationsExtension.FromMarkdown(markdown));
        return pipeline;
    }

    /// <summary>
    /// Renders GitHub-compatible math syntax to static SVG.
    /// </summary>
    public static MarkdownPipelineBuilder UseRenderedMath(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<RenderedMathExtension>();
        return pipeline;
    }

    public static MarkdownPipelineBuilder UseStaticCodeHighlighter(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.Add(new HighlighterExtension());
        return pipeline;
    }

    /// <summary>
    /// Renders GitHub alerts (<c>[!NOTE]</c>, <c>[!TIP]</c>, <c>[!IMPORTANT]</c>, <c>[!WARNING]</c>, <c>[!CAUTION]</c>)
    /// with Markdig's alert blocks, following GitHub's rules.
    /// </summary>
    public static MarkdownPipelineBuilder UseGitHubAlerts(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<AlertExtension>();
        pipeline.Extensions.AddIfNotAlready<GitHubAlertExtension>();
        return pipeline;
    }

    /// <summary>
    /// Renders icon shortcodes, such as <c>:material-account-circle:</c>, and GitHub emoji shortcodes, such as <c>:rocket:</c>.
    /// </summary>
    public static MarkdownPipelineBuilder UseIconsAndEmojis(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<ShortcodeExtension>();
        return pipeline;
    }

    /// <summary>
    /// Renders keyboard shortcuts, such as <c>++ctrl+alt+del++</c>, as keys.
    /// </summary>
    public static MarkdownPipelineBuilder UseKeyboardKeys(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<KeysExtension>();
        return pipeline;
    }

    /// <summary>
    /// Wraps tables in horizontally scrollable containers.
    /// </summary>
    public static MarkdownPipelineBuilder UseScrollableTables(
        this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<ScrollableTableExtension>();
        return pipeline;
    }
}
