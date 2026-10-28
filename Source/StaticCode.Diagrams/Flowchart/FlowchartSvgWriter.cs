using System.Text;
using StaticCode.Diagrams.Layout;
using StaticCode.Diagrams.Svg;
using StaticCode.Diagrams.Text;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.Flowchart;

/// <summary>
/// Writes a laid-out flowchart as inline SVG. The SVG contains no colors: every element carries a
/// <c>diagram-*</c> class so the page stylesheet can theme it for light and dark mode.
/// </summary>
internal static class FlowchartSvgWriter
{
    private const double LoopSize = 22;
    private const double CanvasPadding = 4;

    public static string Write(FlowchartDiagram diagram)
    {
        var horizontal = diagram.Direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;
        var axis = horizontal ? new Point(1, 0) : new Point(0, 1);
        var cross = horizontal ? new Point(0, 1) : new Point(1, 0);

        var nodeIndex = diagram.Nodes.Select((node, index) => (node.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var boxes = diagram.Nodes.Select(NodeShapes.CreateNode).ToList();
        var labels = diagram.Edges.Select(edge => edge.Label == null ? null : NodeShapes.CreateLabel(edge.Label)).ToList();

        var loopSizes = new double[diagram.Edges.Count];
        var margins = new double[boxes.Count];
        var loopCounts = new int[boxes.Count];
        for (var index = 0; index < diagram.Edges.Count; index++)
        {
            var edge = diagram.Edges[index];
            if (edge.From != edge.To) continue;
            var node = nodeIndex[edge.From];
            loopSizes[index] = LoopSize + 10 * loopCounts[node]++;
            var label = labels[index];
            var labelExtent = label == null ? 0 : 6 + (horizontal ? label.Height : label.Width);
            margins[node] = Math.Max(margins[node], loopSizes[index] * 1.1 + labelExtent + 4);
        }

        var layout = new LayeredLayout().Compute(
            boxes.Select((box, index) => new LayoutNode(box.Width, box.Height, margins[index])).ToList(),
            diagram.Edges.Select((edge, index) => new LayoutEdge(
                nodeIndex[edge.From],
                nodeIndex[edge.To],
                edge.Length,
                labels[index]?.Width ?? 0,
                labels[index]?.Height ?? 0)).ToList(),
            diagram.Direction switch
            {
                FlowDirection.BottomToTop => LayoutDirection.BottomToTop,
                FlowDirection.LeftToRight => LayoutDirection.LeftToRight,
                FlowDirection.RightToLeft => LayoutDirection.RightToLeft,
                _ => LayoutDirection.TopToBottom
            });

        var groupBounds = new List<(FlowchartGroup Group, double Left, double Top, double Right, double Bottom)>();
        foreach (var group in diagram.Groups)
        {
            var members = group.NodeIds.Where(nodeIndex.ContainsKey).Select(id => nodeIndex[id]).ToArray();
            if (members.Length == 0) continue;
            groupBounds.Add((group,
                members.Min(index => layout.NodeCenters[index].X - boxes[index].Width / 2) - 18,
                members.Min(index => layout.NodeCenters[index].Y - boxes[index].Height / 2) - 34,
                members.Max(index => layout.NodeCenters[index].X + boxes[index].Width / 2) + 18,
                members.Max(index => layout.NodeCenters[index].Y + boxes[index].Height / 2) + 18));
        }

        var minX = Math.Min(-CanvasPadding, groupBounds.Count == 0 ? -CanvasPadding : groupBounds.Min(group => group.Left));
        var minY = Math.Min(-CanvasPadding, groupBounds.Count == 0 ? -CanvasPadding : groupBounds.Min(group => group.Top));
        var maxX = Math.Max(layout.Width + CanvasPadding, groupBounds.Count == 0 ? layout.Width + CanvasPadding : groupBounds.Max(group => group.Right));
        var maxY = Math.Max(layout.Height + CanvasPadding, groupBounds.Count == 0 ? layout.Height + CanvasPadding : groupBounds.Max(group => group.Bottom));
        var width = maxX - minX;
        var height = maxY - minY;
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-flowchart\"")
            .Append($" viewBox=\"{F(minX)} {F(minY)} {F(width)} {F(height)}\"")
            .Append($" width=\"{F(width)}\" height=\"{F(height)}\"")
            .Append($" font-size=\"{F(TextMetrics.FontSize)}\" role=\"img\" aria-label=\"{Escape(diagram.Title ?? "Flowchart")}\">\n");

        foreach (var bounds in groupBounds)
        {
            svg.Append($"<rect class=\"diagram-subgraph\" x=\"{F(bounds.Left)}\" y=\"{F(bounds.Top)}\" width=\"{F(bounds.Right - bounds.Left)}\" height=\"{F(bounds.Bottom - bounds.Top)}\" rx=\"12\"/>\n")
                .Append($"<text class=\"diagram-subgraph-title\" x=\"{F(bounds.Left + 12)}\" y=\"{F(bounds.Top + 21)}\">{Escape(bounds.Group.Label)}</text>\n");
        }

        var labelCenters = new Point?[diagram.Edges.Count];
        svg.Append("<g class=\"diagram-edges\">\n");
        for (var index = 0; index < diagram.Edges.Count; index++)
        {
            var edge = diagram.Edges[index];
            var from = nodeIndex[edge.From];
            var to = nodeIndex[edge.To];
            if (from == to)
            {
                labelCenters[index] = WriteSelfLoop(svg, edge, boxes[from], layout.NodeCenters[from], loopSizes[index], labels[index], axis, cross, horizontal);
                continue;
            }

            var points = layout.Edges[index].Points.ToList();
            points[0] = SnapToShape(boxes[from], layout.NodeCenters[from], points[0], axis, cross, horizontal);
            points[^1] = SnapToShape(boxes[to], layout.NodeCenters[to], points[^1], axis, cross, horizontal);
            WriteRoute(svg, edge, points, axis);
            labelCenters[index] = layout.Edges[index].LabelCenter;
        }

        svg.Append("</g>\n<g class=\"diagram-edge-labels\">\n");
        for (var index = 0; index < diagram.Edges.Count; index++)
        {
            var label = labels[index];
            if (label == null || labelCenters[index] is not { } center) continue;
            NodeShapes.WriteLabel(svg, label, center);
        }

        svg.Append("</g>\n<g class=\"diagram-nodes\">\n");
        for (var index = 0; index < boxes.Count; index++)
            NodeShapes.WriteNode(svg, boxes[index], layout.NodeCenters[index], href: diagram.Nodes[index].Href);
        svg.Append("</g>\n</svg>");
        return svg.ToString();
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
        return center + cross * (side + size * 1.1 + 6 + labelExtent / 2);
    }
}
