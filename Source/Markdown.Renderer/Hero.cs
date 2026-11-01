using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>
/// The animated hero banner that a <c>hero</c> code block adds to a page. The block holds YAML options;
/// the scene is drawn by a WebGL script that only pages with a hero include.
/// </summary>
public static class Hero
{
    public const string CodeBlockLanguage = "hero";
    public const string FullWidthMarker = "<section class=\"hero hero-full";

    private static readonly Regex HexColorRegex = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);
    private static readonly string[] Times = ["auto", "day", "sunset", "night"];
    private static readonly string[] Qualities = ["auto", "low", "high"];
    private static readonly string[] Effects = ["beach", "connections", "flow", "voronoi", "shader"];
    private static readonly string[] ColorNames = ["water", "shallow", "sand", "accent"];
    private static readonly string[] LinkSchemes = ["http", "https", "mailto"];
    private static readonly char[] ControlCharactersAndSpace = Enumerable.Range(0, 33).Select(c => (char)c).ToArray();
    private static readonly Regex SchemeRegex = new("^(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*):", RegexOptions.Compiled);

    public static string Styles => Resources.HeroCssContent();
    public static string Script => Resources.HeroBeachScriptContent() + Resources.HeroEffectsScriptContent() + Resources.HeroScriptContent();

    public static string ScriptsFor(string renderedHeroes)
    {
        var playground = renderedHeroes.Contains("class=\"hero-playground-template\"", StringComparison.Ordinal);
        return (playground || renderedHeroes.Contains("data-hero-scene=\"beach\"", StringComparison.Ordinal)
                ? Resources.HeroBeachScriptContent() : string.Empty)
            + (playground || new[] { "connections", "flow", "voronoi" }.Any(effect =>
                renderedHeroes.Contains($"data-hero-scene=\"{effect}\"", StringComparison.Ordinal))
                ? Resources.HeroEffectsScriptContent() : string.Empty)
            + Resources.HeroScriptContent()
            + (playground ? Resources.HeroPlaygroundScriptContent() : string.Empty);
    }

    public static string Render(string configuration)
    {
        var root = ReadYaml(configuration);
        var width = Choose(ReadScalar(root, "width"), ["full", "content"]) == "full" ? "full" : "inline";
        var align = Choose(ReadScalar(root, "align"), ["left", "center"]);
        var height = (int)Math.Round(ReadNumber(root, "height", 520, 240, 1200));
        var effect = ReadScalar(root, "effect")?.Trim().ToLowerInvariant() ?? "beach";
        var shader = ReadScalar(root, "shader") ?? string.Empty;
        var playground = bool.TryParse(ReadScalar(root, "playground"), out var interactive) && interactive;
        var error = !Effects.Contains(effect) ? $"Unknown hero effect: {effect}. Choose beach, connections, flow, voronoi, or shader."
            : effect == "shader" && string.IsNullOrWhiteSpace(shader) ? "The shader effect needs GLSL code in the shader option."
            : effect == "shader" && shader.Length > 32768 ? "The hero shader must not exceed 32768 characters."
            : string.Empty;
        if (!Effects.Contains(effect)) effect = "invalid";
        var html = new StringBuilder();
        html.Append($"<section class=\"hero hero-{width} hero-align-{align}\" style=\"--hero-height: {height}px\"");
        html.Append($" data-hero-scene=\"{effect}\"");
        html.Append($" data-hero-time=\"{Choose(ReadScalar(root, "time"), Times)}\"");
        html.Append($" data-hero-quality=\"{Choose(ReadScalar(root, "quality"), Qualities)}\"");
        html.Append($" data-hero-speed=\"{ReadNumber(root, "speed", 1, 0, 4).ToString(CultureInfo.InvariantCulture)}\"");
        html.Append($" data-hero-waves=\"{ReadNumber(root, "waves", 1, 0, 2.5).ToString(CultureInfo.InvariantCulture)}\"");
        if (root?.Children.TryGetValue(new YamlScalarNode("colors"), out var colorsNode) == true && colorsNode is YamlMappingNode colors)
        {
            foreach (var name in ColorNames)
            {
                var color = ReadScalar(colors, name)?.Trim();
                if (color is not null && HexColorRegex.IsMatch(color)) html.Append($" data-hero-{name}=\"{color}\"");
            }
        }
        html.AppendLine(">");
        html.AppendLine("<canvas class=\"hero-canvas\" aria-hidden=\"true\"></canvas>");
        if (shader.Length > 0 && shader.Length <= 32768)
            html.AppendLine($"<template class=\"hero-shader\">{HtmlEncoder.Default.Encode(shader)}</template>");
        html.AppendLine("<div class=\"hero-content\">");

        var title = ReadScalar(root, "title");
        if (!string.IsNullOrWhiteSpace(title))
            html.AppendLine($"<p class=\"hero-title\">{HtmlEncoder.Default.Encode(title.Trim())}</p>");
        var text = ReadScalar(root, "text");
        if (!string.IsNullOrWhiteSpace(text))
            html.AppendLine($"<p class=\"hero-text\">{HtmlEncoder.Default.Encode(text.Trim())}</p>");

        var actions = ReadActions(root);
        if (actions.Count > 0)
        {
            html.Append("<div class=\"hero-actions\">");
            foreach (var action in actions)
            {
                var external = Uri.TryCreate(action.Link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
                html.Append($"<a class=\"hero-button{(action.Primary ? " hero-button-primary" : string.Empty)}\" href=\"{HtmlEncoder.Default.Encode(RewriteMarkdownLink(action.Link))}\"");
                if (playground) html.Append($" data-hero-link=\"{HtmlEncoder.Default.Encode(action.Link)}\"");
                if (external) html.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
                html.Append($">{HtmlEncoder.Default.Encode(action.Text)}</a>");
            }
            html.AppendLine("</div>");
        }

        html.AppendLine("</div>");
        html.Append($"<p class=\"hero-error\" role=\"status\"{(error.Length == 0 ? " hidden=\"hidden\"" : string.Empty)}>");
        html.Append(HtmlEncoder.Default.Encode(error));
        html.AppendLine("</p>");
        if (playground)
            html.AppendLine(PlaygroundTemplate);
        html.Append("</section>");
        return html.ToString();
    }

    /// <summary>
    /// Moves the first full-width hero out of the rendered page content so that the page template can place it
    /// above the sidebar and content columns.
    /// </summary>
    public static string ExtractFullWidth(ref string renderedMarkdown)
    {
        var start = renderedMarkdown.IndexOf(FullWidthMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        const string end = "</section>";
        var endIndex = renderedMarkdown.IndexOf(end, start, StringComparison.Ordinal);
        if (endIndex < 0) return string.Empty;
        endIndex += end.Length;
        var hero = renderedMarkdown[start..endIndex];
        var position = hero.Contains("class=\"hero-playground-template\"", StringComparison.Ordinal)
            ? "<template class=\"hero-content-position\"></template>" : string.Empty;
        // Only one hero can span the window; any further ones stay in the page text.
        renderedMarkdown = renderedMarkdown.Remove(start, endIndex - start)
            .Insert(start, position)
            .Replace(FullWidthMarker, "<section class=\"hero hero-inline", StringComparison.Ordinal);
        return hero;
    }

    private static YamlMappingNode? ReadYaml(string configuration)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(configuration));
            return stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<HeroAction> ReadActions(YamlMappingNode? root)
    {
        if (root?.Children.TryGetValue(new YamlScalarNode("actions"), out var node) != true || node is not YamlSequenceNode sequence)
            return Array.Empty<HeroAction>();

        var actions = new List<HeroAction>();
        foreach (var item in sequence.Children.OfType<YamlMappingNode>())
        {
            var text = ReadScalar(item, "text");
            var link = SafeLink(ReadScalar(item, "link"));
            if (string.IsNullOrWhiteSpace(text) || link is null) continue;
            var style = ReadScalar(item, "style");
            var primary = style is null ? actions.Count == 0 : style.Equals("primary", StringComparison.OrdinalIgnoreCase);
            actions.Add(new HeroAction(text.Trim(), link, primary));
        }

        return actions;
    }

    /// <summary>
    /// Returns the link as a browser reads it, or <c>null</c> when it would run script or load data instead of
    /// navigating. Browsers drop tabs and line breaks inside URLs and trim control characters around them, so
    /// the scheme is checked after the same cleanup. Only relative links, http, https, and mailto are kept.
    /// </summary>
    private static string? SafeLink(string? link)
    {
        if (link is null) return null;
        var cleaned = new string(link.Where(c => c is not ('\t' or '\n' or '\r')).ToArray()).Trim(ControlCharactersAndSpace);
        if (cleaned.Length == 0) return null;
        var scheme = SchemeRegex.Match(cleaned);
        return !scheme.Success || LinkSchemes.Contains(scheme.Groups["scheme"].Value.ToLowerInvariant()) ? cleaned : null;
    }

    /// <summary>
    /// Points relative Markdown links at the rendered pages, like links in ordinary Markdown text.
    /// </summary>
    private static string RewriteMarkdownLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out _) || link.StartsWith('/') || link.StartsWith('#')) return link;
        var suffixStart = link.IndexOfAny(['?', '#']);
        var path = suffixStart < 0 ? link : link[..suffixStart];
        var suffix = suffixStart < 0 ? string.Empty : link[suffixStart..];
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return link;

        var fileStart = path.LastIndexOf('/') + 1;
        var name = path[fileStart..^3];
        if (name.Equals("readme", StringComparison.OrdinalIgnoreCase)) name = "index";
        return path[..fileStart] + name + ".html" + suffix;
    }

    private static string Choose(string? value, string[] allowed)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && allowed.Contains(normalized) ? normalized : allowed[0];
    }

    private static double ReadNumber(YamlMappingNode? root, string key, double fallback, double min, double max)
    {
        var value = ReadScalar(root, key)?.Trim();
        if (value is not null && value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? Math.Clamp(number, min, max)
            : fallback;
    }

    private static string? ReadScalar(YamlMappingNode? mapping, string key) =>
        mapping?.Children.TryGetValue(new YamlScalarNode(key), out var value) == true && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private const string PlaygroundTemplate = """
        <template class="hero-playground-template">
        <form class="hero-playground" aria-label="Hero playground">
            <div class="hero-playground-toolbar">
                <strong>Hero playground</strong>
                <div class="hero-control-actions">
                    <button type="button" name="preview">View preview</button>
                    <button type="button" name="pause" aria-pressed="false">Pause</button>
                    <button type="button" name="reset">Reset</button>
                    <button type="button" name="copy" class="hero-copy">Copy YAML</button>
                </div>
            </div>
            <fieldset>
                <legend>Effect &amp; preview</legend>
                <div class="hero-controls">
                    <label>Effect<select name="effect">
                        <option value="beach">Beach</option>
                        <option value="connections">Connections</option>
                        <option value="flow">Flow</option>
                        <option value="voronoi">Voronoi</option>
                        <option value="shader">Own shader (advanced)</option>
                    </select></label>
                    <label>Width<select name="width">
                        <option value="full">Full width</option>
                        <option value="content">Content width</option>
                    </select></label>
                    <label>Site theme<select name="theme">
                        <option value="auto">Auto</option>
                        <option value="light">Light</option>
                        <option value="dark">Dark</option>
                    </select></label>
                    <label>Speed<input name="speed" type="range" min="0" max="4" step="0.1" /><output class="hero-speed"></output></label>
                </div>
            </fieldset>
            <details class="hero-layout-options">
                <summary>Text &amp; layout</summary>
                <div class="hero-controls hero-content-controls">
                    <label>Title<input name="title" type="text" /></label>
                    <label>Description<input name="text" type="text" /></label>
                </div>
                <div class="hero-controls">
                    <label>Height<input name="height" type="range" min="240" max="1200" step="10" /><output class="hero-height"></output></label>
                    <label>Alignment<select name="align"><option value="left">Left</option><option value="center">Center</option></select></label>
                    <label>Quality<select name="quality"><option value="auto">Auto</option><option value="low">Low</option><option value="high">High</option></select></label>
                </div>
            </details>
            <details class="hero-appearance-options">
                <summary>Scene &amp; colors</summary>
                <fieldset class="hero-beach-controls" hidden="hidden">
                    <legend>Beach scene</legend>
                    <div class="hero-controls">
                        <label>Lighting<select name="time">
                            <option value="auto">Auto</option><option value="day">Day</option><option value="sunset">Sunset</option><option value="night">Night</option>
                        </select></label>
                        <label>Waves<input name="waves" type="range" min="0" max="2.5" step="0.1" /><output class="hero-waves"></output></label>
                        <label>Water<input name="water" type="color" /></label>
                        <label>Shallows<input name="shallow" type="color" /></label>
                        <label>Sand<input name="sand" type="color" /></label>
                    </div>
                </fieldset>
                <div class="hero-controls hero-color-controls">
                    <label class="hero-accent-control">Accent<input name="accent" type="color" /></label>
                    <button type="button" name="colorsReset">Use default colors</button>
                </div>
            </details>
            <details class="hero-shader-editor" hidden="hidden">
                <summary>Own shader (advanced)</summary>
                <p>The example draws an animated hexagonal tunnel with depth and glowing edges. Edit it to make your own effect.</p>
                <label>GLSL<textarea name="shader" rows="12" maxlength="32768" spellcheck="false"></textarea></label>
                <p>Changes preview automatically after a short typing pause. Only run shaders you trust; expensive shaders can stall the GPU.</p>
            </details>
            <details class="hero-yaml-options">
                <summary>Hero YAML</summary>
                <label>Generated Markdown<textarea name="yaml" rows="8" readonly="readonly" spellcheck="false"></textarea></label>
            </details>
            <p class="hero-playground-status" role="status"></p>
        </form>
        </template>
        """;

    private sealed record HeroAction(string Text, string Link, bool Primary);
}
