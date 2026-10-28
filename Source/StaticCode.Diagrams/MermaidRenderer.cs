using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    public static string RenderSvg(string source) => RenderSvg(source, 0);

    /// <summary>Renders Mermaid SVG with a page-local instance number for unique accessibility references.</summary>
    public static string RenderSvg(string source, int instance)
    {
        source = PrepareSource(source);
        if (source.TrimStart().StartsWith("classDiagram", StringComparison.Ordinal)) return ClassDiagramRenderer.Render(source);
        if (FlowchartParser.CanParse(source)) return FlowchartSvgWriter.Write(FlowchartParser.Parse(source));
        if (StateParser.CanParse(source)) return StateSvgWriter.Write(StateParser.Parse(source), $"state-{DiagramRenderer.StableHash(source):x8}-{instance.ToString(CultureInfo.InvariantCulture)}");
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
        var lines = source.Split('\n');
        var start = 0;
        while (start < lines.Length && (lines[start].Trim().Length == 0 || lines[start].TrimStart().StartsWith("%%", StringComparison.Ordinal))) start++;
        if (start < lines.Length && lines[start].Trim() == "---")
        {
            for (var end = start + 1; end < lines.Length; end++)
                if (lines[end].Trim() == "---") return string.Join('\n', lines.Skip(end + 1));
        }

        return source;
    }
}
