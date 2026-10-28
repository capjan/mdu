using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using StaticCode.Diagrams.Component;
using StaticCode.Diagrams.Context;
using StaticCode.Diagrams.Isometric;

namespace StaticCode.Diagrams;

/// <summary>
/// Renders fenced code blocks that hold diagrams: <c>mermaid</c> for the supported Mermaid subset,
/// <c>plantuml</c> (or <c>puml</c>) for PlantUML component diagrams, <c>context</c> for context diagrams, and
/// <c>isometric</c> for isometric diagrams with phases.
/// </summary>
public static class DiagramRenderer
{
    public const string IsometricLanguage = "isometric";

    /// <summary>Appears in rendered HTML exactly when an isometric diagram has phase controls.</summary>
    public const string IsometricPhaseControlsMarker = "class=\"iso-controls\"";

    /// <exception cref="DiagramSyntaxException">The source is not a supported context diagram.</exception>
    public static string RenderContextSvg(string source) => ContextSvgWriter.Write(ContextParser.Parse(source));

    /// <summary>Returns the phase controls and the SVG; pages with these diagrams include the playback script.</summary>
    /// <param name="source">The diagram source.</param>
    /// <param name="instance">
    /// Tells diagrams on the same page apart, such as the position of the code block in its document, so that the
    /// phase controls of two equal diagrams do not share a radio group.
    /// </param>
    /// <exception cref="DiagramSyntaxException">The source is not a supported isometric diagram.</exception>
    public static string RenderIsometricHtml(string source, int instance = 0) =>
        IsometricSvgWriter.Write(IsometricParser.Parse(source), $"iso-{StableHash(source):x8}-{instance.ToString(CultureInfo.InvariantCulture)}");

    /// <exception cref="DiagramSyntaxException">The source is not a supported PlantUML component diagram.</exception>
    public static string RenderPlantUmlSvg(string source) => ComponentSvgWriter.Write(ComponentParser.Parse(source));

    /// <summary>
    /// Returns <c>false</c> for other languages and for diagrams outside the supported syntax. Isometric diagrams are
    /// HTML rather than SVG, so they come from <see cref="TryRenderHtml"/> instead.
    /// </summary>
    public static bool TryRenderSvg(string language, string source, [NotNullWhen(true)] out string? svg) =>
        TryRenderSvg(language, source, 0, out svg);

    private static bool TryRenderSvg(string language, string source, int instance, [NotNullWhen(true)] out string? svg)
    {
        try
        {
            svg = language.ToLowerInvariant() switch
            {
                "mermaid" => MermaidRenderer.RenderSvg(source, instance),
                "context" => RenderContextSvg(source),
                "plantuml" or "puml" => RenderPlantUmlSvg(source),
                _ => null
            };
        }
        catch (DiagramSyntaxException)
        {
            svg = null;
        }

        return svg != null;
    }

    /// <summary>
    /// Renders a diagram code block for a page: the phase controls and SVG of an isometric diagram, or the SVG of the
    /// other diagram types. Returns <c>false</c> for other languages and for diagrams outside the supported syntax.
    /// </summary>
    /// <param name="language">The code block language.</param>
    /// <param name="source">The diagram source.</param>
    /// <param name="instance">Tells diagrams on the same page apart; see <see cref="RenderIsometricHtml"/>.</param>
    /// <param name="html">The rendered diagram.</param>
    public static bool TryRenderHtml(string language, string source, int instance, [NotNullWhen(true)] out string? html)
    {
        if (!language.Equals(IsometricLanguage, StringComparison.OrdinalIgnoreCase))
            return TryRenderSvg(language, source, instance, out html);

        try
        {
            html = RenderIsometricHtml(source, instance);
            return true;
        }
        catch (DiagramSyntaxException)
        {
            html = null;
            return false;
        }
    }

    /// <summary>FNV-1a, which unlike <see cref="string.GetHashCode()"/> stays the same between runs.</summary>
    internal static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text) hash = (hash ^ character) * 16777619u;
        return hash;
    }
}
