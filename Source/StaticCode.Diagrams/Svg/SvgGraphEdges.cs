using System.Text;
using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.Layout;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.Svg;

/// <summary>Shared node spacing, edge outlines, and curves for layered graph diagrams.</summary>
internal static class SvgGraphEdges
{
    private const double LoopSize = 22;
    private const double LoopSeparation = 10;
    private const double LoopReach = 1.1;
    private const double LabelGap = 6;
    private const double EdgeMargin = 4;

    public static (LayoutNode[] Nodes, double[] LoopSizes) SizeNodes(
        IReadOnlyList<NodeBox> boxes, IReadOnlyList<FlowchartEdge> edges, IReadOnlyList<LabelBox?> labels,
        IReadOnlyDictionary<string, int> indices, bool horizontal)
    {
        var margins = new double[boxes.Count];
        var loops = new double[edges.Count];
        var loopExtents = new double[boxes.Count];
        var alongSizes = boxes.Select(box => horizontal ? box.Width : box.Height).ToArray();
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            if (edge.From != edge.To) continue;
            var node = indices[edge.From];
            loops[index] = LoopSize + loopExtents[node];
            var label = labels[index];
            var labelExtent = label == null ? 0 : horizontal ? label.Height : label.Width;
            alongSizes[node] = Math.Max(alongSizes[node], label == null ? 0 : horizontal ? label.Width : label.Height);
            loopExtents[node] += LoopSeparation + labelExtent / LoopReach;
            margins[node] = Math.Max(margins[node], loops[index] * LoopReach + labelExtent + LabelGap + EdgeMargin);
        }
        var nodes = boxes.Select((box, index) => new LayoutNode(
            horizontal ? alongSizes[index] : box.Width, horizontal ? box.Height : alongSizes[index], margins[index])).ToArray();
        return (nodes, loops);
    }

    /// <summary>Moves an edge end from the node's bounding box onto its actual outline.</summary>
    internal static Point SnapToShape(NodeBox box, Point center, Point point, Point axis, Point cross, bool horizontal)
    {
        var offset = point - center;
        var along = offset.Dot(axis);
        var across = offset.Dot(cross);
        var halfAlong = (horizontal ? box.Width : box.Height) / 2;
        var halfAcross = (horizontal ? box.Height : box.Width) / 2;
        var extent = NodeShapes.Extent(box, halfAlong, halfAcross, across);
        return center + cross * across + axis * ((along < 0 ? -1 : 1) * extent);
    }

    /// <summary>
    /// Draws a smooth cubic Hermite spline through the route. Interior tangents follow Catmull-Rom;
    /// the ends leave and enter nodes along the flow direction.
    /// </summary>
    internal static void WriteRoute(StringBuilder svg, FlowchartEdge edge, List<Point> points, Point axis)
    {
        var startDirection = axis * Math.Sign((points[1] - points[0]).Dot(axis));
        var endDirection = axis * Math.Sign((points[^1] - points[^2]).Dot(axis));
        var start = points[0];
        var end = points[^1];
        points[0] = start + startDirection * SvgMarkers.Inset(NodeShapes.Marker(edge.StartMarker), NodeShapes.MarkerScale(edge.Style));
        points[^1] = end - endDirection * SvgMarkers.Inset(NodeShapes.Marker(edge.EndMarker), NodeShapes.MarkerScale(edge.Style));

        var count = points.Count;
        var tangents = new Point[count];
        tangents[0] = axis * ((points[1] - points[0]).Dot(axis) * 1.5);
        tangents[^1] = axis * ((points[^1] - points[^2]).Dot(axis) * 1.5);
        for (var index = 1; index < count - 1; index++)
            tangents[index] = (points[index + 1] - points[index - 1]) * 0.5;

        var path = new StringBuilder($"M{F(points[0].X)} {F(points[0].Y)}");
        for (var index = 0; index < count - 1; index++)
        {
            var c1 = points[index] + tangents[index] * (1.0 / 3);
            var c2 = points[index + 1] - tangents[index + 1] * (1.0 / 3);
            path.Append($" C{F(c1.X)} {F(c1.Y)} {F(c2.X)} {F(c2.Y)} {F(points[index + 1].X)} {F(points[index + 1].Y)}");
        }

        svg.Append($"<path class=\"{NodeShapes.EdgeClass(edge.Style)}\" d=\"{path}\"/>");
        SvgMarkers.Write(svg, NodeShapes.Marker(edge.EndMarker), end, endDirection, NodeShapes.MarkerScale(edge.Style));
        SvgMarkers.Write(svg, NodeShapes.Marker(edge.StartMarker), start, startDirection * -1, NodeShapes.MarkerScale(edge.Style));
        svg.Append('\n');
    }

    internal static Point? WriteSelfLoop(
        StringBuilder svg, FlowchartEdge edge, NodeBox box, Point center, double size, LabelBox? label, Point axis, Point cross, bool horizontal)
    {
        var halfAlong = (horizontal ? box.Width : box.Height) / 2;
        var halfAcross = (horizontal ? box.Height : box.Width) / 2;
        var attach = Math.Min(halfAlong * 0.45, 10);
        var side = NodeShapes.Extent(box, halfAcross, halfAlong, attach);
        var start = center - axis * attach + cross * side;
        var end = center + axis * attach + cross * side;
        var c1 = start + cross * (size * 1.4) - axis * (size * 0.5);
        var c2 = end + cross * (size * 1.4) + axis * (size * 0.5);
        var endDirection = (end - c2).Normalized;
        var startDirection = (start - c1).Normalized;
        var pathEnd = end - endDirection * SvgMarkers.Inset(NodeShapes.Marker(edge.EndMarker), NodeShapes.MarkerScale(edge.Style));
        var pathStart = start - startDirection * SvgMarkers.Inset(NodeShapes.Marker(edge.StartMarker), NodeShapes.MarkerScale(edge.Style));

        svg.Append($"<path class=\"{NodeShapes.EdgeClass(edge.Style)}\" d=\"M{F(pathStart.X)} {F(pathStart.Y)} C{F(c1.X)} {F(c1.Y)} {F(c2.X)} {F(c2.Y)} {F(pathEnd.X)} {F(pathEnd.Y)}\"/>");
        SvgMarkers.Write(svg, NodeShapes.Marker(edge.EndMarker), end, endDirection, NodeShapes.MarkerScale(edge.Style));
        SvgMarkers.Write(svg, NodeShapes.Marker(edge.StartMarker), start, startDirection, NodeShapes.MarkerScale(edge.Style));
        svg.Append('\n');

        if (label == null) return null;
        var labelExtent = horizontal ? label.Height : label.Width;
        return center + cross * (side + size * LoopReach + LabelGap + labelExtent / 2);
    }
}
