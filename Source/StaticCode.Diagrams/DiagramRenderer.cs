using System.Diagnostics.CodeAnalysis;
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

    /// <exception cref="DiagramSyntaxException">The source is not a supported context diagram.</exception>
    public static string RenderContextSvg(string source) => ContextSvgWriter.Write(ContextParser.Parse(source));

    /// <summary>Returns the phase controls and the SVG; pages with these diagrams include the playback script.</summary>
    /// <exception cref="DiagramSyntaxException">The source is not a supported isometric diagram.</exception>
    public static string RenderIsometricHtml(string source) => IsometricSvgWriter.Write(IsometricParser.Parse(source));

    /// <exception cref="DiagramSyntaxException">The source is not a supported PlantUML component diagram.</exception>
    public static string RenderPlantUmlSvg(string source) => ComponentSvgWriter.Write(ComponentParser.Parse(source));

    /// <summary>Returns <c>false</c> for other languages and for diagrams outside the supported syntax.</summary>
    public static bool TryRenderSvg(string language, string source, [NotNullWhen(true)] out string? svg)
    {
        try
        {
            svg = language.ToLowerInvariant() switch
            {
                "mermaid" => MermaidRenderer.RenderSvg(source),
                "context" => RenderContextSvg(source),
                "plantuml" or "puml" => RenderPlantUmlSvg(source),
                IsometricLanguage => RenderIsometricHtml(source),
                _ => null
            };
        }
        catch (DiagramSyntaxException)
        {
            svg = null;
        }

        return svg != null;
    }
}
