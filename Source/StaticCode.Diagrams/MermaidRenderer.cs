using System.Diagnostics.CodeAnalysis;
using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.Sequence;
using StaticCode.Diagrams.State;

namespace StaticCode.Diagrams;

/// <summary>
/// Renders a supported subset of Mermaid diagram syntax to static, themeable SVG without JavaScript.
/// </summary>
public static class MermaidRenderer
{
    /// <exception cref="DiagramSyntaxException">The source is not a supported diagram.</exception>
    public static string RenderSvg(string source)
    {
        source = PrepareSource(source);
        if (source.TrimStart().StartsWith("classDiagram", StringComparison.Ordinal)) return ClassDiagramRenderer.Render(source);
        if (FlowchartParser.CanParse(source)) return FlowchartSvgWriter.Write(FlowchartParser.Parse(source));
        if (StateParser.CanParse(source)) return StateSvgWriter.Write(StateParser.Parse(source));
        if (SequenceParser.CanParse(source)) return SequenceSvgWriter.Write(SequenceParser.Parse(source));
        throw new DiagramSyntaxException("Unsupported Mermaid diagram syntax.", 1);
    }

    public static bool TryRenderSvg(string source, [NotNullWhen(true)] out string? svg)
    {
        try
        {
            svg = RenderSvg(source);
            return true;
        }
        catch (DiagramSyntaxException)
        {
            svg = null;
            return false;
        }
    }

    private static string PrepareSource(string source)
    {
        source = source.ReplaceLineEndings("\n");
        if (source.TrimStart().StartsWith("---\n", StringComparison.Ordinal))
        {
            var start = source.IndexOf("---\n", StringComparison.Ordinal);
            var end = source.IndexOf("\n---", start + 4, StringComparison.Ordinal);
            if (end >= 0) source = source.Remove(start, end + 4 - start).TrimStart('\n');
        }

        return source;
    }
}
