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
    private const double CanvasPadding = 4;

    public static string Write(FlowchartDiagram diagram)
    {
        var horizontal = diagram.Direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;
        var axis = horizontal ? new Point(1, 0) : new Point(0, 1);
        var cross = horizontal ? new Point(0, 1) : new Point(1, 0);

        var nodeIndex = diagram.Nodes.Select((node, index) => (node.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var boxes = diagram.Nodes.Select(NodeShapes.CreateNode).ToList();
        var labels = diagram.Edges.Select(edge => edge.Label == null ? null : NodeShapes.CreateLabel(edge.Label)).ToList();

        var (nodes, loopSizes) = SvgGraphEdges.SizeNodes(boxes, diagram.Edges, labels, nodeIndex, horizontal);

        var layout = new LayeredLayout().Compute(
            nodes,
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
                labelCenters[index] = SvgGraphEdges.WriteSelfLoop(svg, edge, boxes[from], layout.NodeCenters[from], loopSizes[index], labels[index], axis, cross, horizontal);
                continue;
            }

            var points = layout.Edges[index].Points.ToList();
            points[0] = SvgGraphEdges.SnapToShape(boxes[from], layout.NodeCenters[from], points[0], axis, cross, horizontal);
            points[^1] = SvgGraphEdges.SnapToShape(boxes[to], layout.NodeCenters[to], points[^1], axis, cross, horizontal);
            SvgGraphEdges.WriteRoute(svg, edge, points, axis);
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
}
