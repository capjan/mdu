using Markdig;
using Markdig.Extensions.Emoji;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using StaticCode.Icons;

namespace Markdown.Renderer.MarkdigExtension;

/// <summary>
/// Renders icon shortcodes such as <c>:material-account-circle:</c> as inline SVG and GitHub emoji shortcodes
/// such as <c>:rocket:</c> as Unicode emojis. Unknown shortcodes stay text. An attribute list directly after a
/// shortcode, for example <c>:octicons-heart-fill-24:{ .heart }</c>, is applied to the icon or emoji.
/// </summary>
public class ShortcodeExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.InlineParsers.AddIfNotAlready<ShortcodeParser>();
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is not Markdig.Renderers.HtmlRenderer htmlRenderer) return;
        htmlRenderer.ObjectRenderers.AddIfNotAlready<IconInlineRenderer>();
        htmlRenderer.ObjectRenderers.AddIfNotAlready<EmojiShortcodeInlineRenderer>();
    }

    /// <summary>All GitHub emoji shortcodes with colons, for example <c>:rocket:</c>, and their Unicode emoji.</summary>
    public static IReadOnlyDictionary<string, string> Emojis { get; } = 
        new Dictionary<string, string>(EmojiMapping.GetDefaultEmojiShortcodeToUnicode());
}

public class IconInline : LeafInline
{
    public IconInline(Icon icon) => Icon = icon;
    public Icon Icon { get; }
}

public class EmojiShortcodeInline : LeafInline
{
    public EmojiShortcodeInline(string shortcode, string emoji)
    {
        Shortcode = shortcode;
        Emoji = emoji;
    }

    public string Shortcode { get; }
    public string Emoji { get; }
}

public class ShortcodeParser : InlineParser
{
    private const int MaxNameLength = 100;

    public ShortcodeParser() => OpeningCharacters = new[] { ':' };

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var text = slice.Text;
        var nameStart = slice.Start + 1;
        var position = nameStart;
        while (position <= slice.End && position - nameStart <= MaxNameLength && IsNameCharacter(text[position]))
            position++;

        if (position == nameStart || position > slice.End || text[position] != ':') return false;

        var name = text.Substring(nameStart, position - nameStart);
        Inline inline;
        if (IconCatalog.TryGetIcon(name, out var icon))
            inline = new IconInline(icon);
        else if (ShortcodeExtension.Emojis.TryGetValue($":{name}:", out var emoji))
            inline = new EmojiShortcodeInline($":{name}:", emoji);
        else
            return false;

        var start = processor.GetSourcePosition(slice.Start, out var line, out var column);
        inline.Span = new SourceSpan(start, start + position - slice.Start);
        inline.Line = line;
        inline.Column = column;
        processor.Inline = inline;
        slice.Start = position + 1;
        return true;
    }

    private static bool IsNameCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '+';
}

public class IconInlineRenderer : HtmlObjectRenderer<IconInline>
{
    protected override void Write(Markdig.Renderers.HtmlRenderer renderer, IconInline obj)
    {
        // Plain text output, for example for heading identifiers, has no room for an icon.
        if (!renderer.EnableHtmlForInline) return;
        var attributes = ShortcodeAttributes.With(obj, "icon");
        if (ShortcodeAttributes.HasColor(obj)) attributes.AddClass("icon-custom-color");
        renderer.Write("<span").WriteAttributes(attributes).Write('>')
            .Write(obj.Icon.ToSvg())
            .Write("</span>");
    }
}

public class EmojiShortcodeInlineRenderer : HtmlObjectRenderer<EmojiShortcodeInline>
{
    protected override void Write(Markdig.Renderers.HtmlRenderer renderer, EmojiShortcodeInline obj)
    {
        if (!renderer.EnableHtmlForInline)
        {
            renderer.Write(obj.Emoji);
            return;
        }

        var attributes = ShortcodeAttributes.With(obj, "emoji");
        attributes.AddPropertyIfNotExist("title", obj.Shortcode);
        renderer.Write("<span").WriteAttributes(attributes).Write('>')
            .Write(obj.Emoji)
            .Write("</span>");
    }
}

internal static class ShortcodeAttributes
{
    public static bool HasColor(Inline inline)
    {
        var properties = inline.TryGetAttributes()?.Properties;
        if (properties is null) return false;

        foreach (var (name, value) in properties)
        {
            if (!name.Equals("style", StringComparison.OrdinalIgnoreCase) || value is null) continue;
            foreach (var declaration in value.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator > 0 && declaration[..separator].Trim().Equals("color", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static HtmlAttributes With(Inline inline, string className)
    {
        var attributes = new HtmlAttributes();
        attributes.AddClass(className);
        inline.TryGetAttributes()?.CopyTo(attributes, mergeIdAndProperties: true);
        return attributes;
    }
}
