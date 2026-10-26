using System.Xml;
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
        var svg = obj.Icon.ToSvg();
        if (obj.Icon.Shortcode.StartsWith("dashboard-", StringComparison.Ordinal)
            && ShortcodeAttributes.TryGetColor(obj, out var color))
            svg = IconSvgColorizer.Apply(svg, color);

        var attributes = ShortcodeAttributes.With(obj, "icon");
        renderer.Write("<span").WriteAttributes(attributes).Write('>')
            .Write(svg)
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
    public static bool TryGetColor(Inline inline, out string color)
    {
        var properties = inline.TryGetAttributes()?.Properties;
        if (properties is null)
        {
            color = string.Empty;
            return false;
        }

        foreach (var (name, value) in properties)
        {
            if (!name.Equals("style", StringComparison.OrdinalIgnoreCase) || value is null) continue;
            foreach (var declaration in value.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0 || !declaration[..separator].Trim().Equals("color", StringComparison.OrdinalIgnoreCase)) continue;
                color = declaration[(separator + 1)..].Trim();
                var important = color.LastIndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (important >= 0) color = color[..important].TrimEnd();
                if (color.Length > 0 && !color.Contains("url(", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        color = string.Empty;
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

internal static class IconSvgColorizer
{
    private static readonly HashSet<string> StructuralElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "defs", "mask", "clippath", "filter", "lineargradient", "radialgradient", "pattern", "marker"
    };

    public static string Apply(string svg, string color)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml($"<icon-root>{svg}</icon-root>");
        foreach (var element in document.DocumentElement!.ChildNodes.OfType<XmlElement>())
            Colorize(element, color);
        return document.DocumentElement.InnerXml;
    }

    private static void Colorize(XmlElement element, string color)
    {
        if (StructuralElements.Contains(element.LocalName) || HasProperty(element, "filter") || HasPartialOpacity(element)) return;

        ColorizeProperty(element, "fill", color);
        ColorizeProperty(element, "stroke", color);

        foreach (var child in element.ChildNodes.OfType<XmlElement>())
            Colorize(child, color);
    }

    private static string? PropertyValue(XmlElement element, string property)
    {
        var style = element.GetAttribute("style");
        foreach (var declaration in style.Split(';'))
        {
            var separator = declaration.IndexOf(':');
            if (separator > 0 && declaration[..separator].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                return StripImportant(declaration[(separator + 1)..].Trim());
        }

        return element.HasAttribute(property) ? element.GetAttribute(property) : null;
    }

    private static bool HasProperty(XmlElement element, string property) =>
        PropertyValue(element, property) is { Length: > 0 };

    private static void ColorizeProperty(XmlElement element, string property, string color)
    {
        if (PropertyValue(element, property) is not { } value || !CanColorize(value)) return;

        var declarations = element.GetAttribute("style").Split(';');
        for (var index = 0; index < declarations.Length; index++)
        {
            var separator = declarations[index].IndexOf(':');
            if (separator <= 0 || !declarations[index][..separator].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)) continue;
            declarations[index] = $"{declarations[index][..(separator + 1)]}{color}";
            element.SetAttribute("style", string.Join(';', declarations));
            if (element.HasAttribute(property)) element.SetAttribute(property, color);
            return;
        }

        element.SetAttribute(property, color);
    }

    private static string StripImportant(string value)
    {
        var important = value.LastIndexOf("!important", StringComparison.OrdinalIgnoreCase);
        return important >= 0 ? value[..important].TrimEnd() : value;
    }

    private static bool CanColorize(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
            || value.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || value.Equals("unset", StringComparison.OrdinalIgnoreCase)
            || value.Equals("revert", StringComparison.OrdinalIgnoreCase)) return false;

        return !IsWhite(value) && !HasEmbeddedTransparency(value);
    }

    private static bool IsWhite(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value == "white") return true;
        if (!value.StartsWith('#')) return false;

        var hex = value[1..];
        if (hex.Length == 4) hex = hex[..3];
        if (hex.Length == 8) hex = hex[..6];
        if (hex.Length == 3) hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length != 6) return false;

        for (var index = 0; index < 6; index += 2)
            if (!byte.TryParse(hex.AsSpan(index, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var channel) || channel < 250)
                return false;

        return true;
    }

    private static bool HasEmbeddedTransparency(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value.StartsWith('#') && value.Length == 5 && value[4] != 'f') return true;
        if (value.StartsWith('#') && value.Length == 9
            && byte.TryParse(value.AsSpan(7, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var alpha)
            && alpha < 255) return true;

        return value.StartsWith("rgba(", StringComparison.Ordinal)
            || value.StartsWith("hsla(", StringComparison.Ordinal)
            || value.Contains('/') && (value.StartsWith("rgb(", StringComparison.Ordinal)
                || value.StartsWith("hsl(", StringComparison.Ordinal)
                || value.StartsWith("color(", StringComparison.Ordinal));
    }

    private static bool HasPartialOpacity(XmlElement element) =>
        IsPartialOpacity(PropertyValue(element, "opacity"))
        || IsPartialOpacity(PropertyValue(element, "fill-opacity"))
        || IsPartialOpacity(PropertyValue(element, "stroke-opacity"));

    private static bool IsPartialOpacity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var opacity = value.Trim();
        var isPercent = opacity.EndsWith('%');
        if (isPercent) opacity = opacity[..^1].Trim();
        if (!double.TryParse(opacity, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) return true;
        return isPercent ? number < 100 : number < 1;
    }
}
