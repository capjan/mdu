using System.Text;
using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.Layout;
using StaticCode.Diagrams.Svg;
using StaticCode.Diagrams.Text;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.State;

/// <summary>Sizes composite states from the inside out and draws them with the shared flowchart primitives.</summary>
internal static class StateSvgWriter
{
    private const double Padding = 18;
    private const double CanvasPadding = 4;

    public static string Write(StateDiagram diagram)
    {
        var scene = Arrange(diagram);
        var width = scene.Layout.Width + 2 * CanvasPadding;
        var height = scene.Layout.Height + 2 * CanvasPadding;
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-state\"")
            .Append($" viewBox=\"{-CanvasPadding} {-CanvasPadding} {F(width)} {F(height)}\"")
            .Append($" width=\"{F(width)}\" height=\"{F(height)}\"")
            .Append($" font-size=\"{F(TextMetrics.FontSize)}\" role=\"group\" aria-label=\"{Escape(diagram.Title ?? "State diagram")}\">\n");
        WriteScene(svg, scene, new Point(0, 0));
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static Scene Arrange(StateDiagram diagram)
    {
        var horizontal = diagram.Direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;
        var children = diagram.Nodes.Select(node => node.Content == null ? null : Arrange(node.Content)).ToArray();
        var boxes = diagram.Nodes.Select((node, index) => CreateBox(node, children[index], horizontal)).ToArray();
        var labels = diagram.Edges.Select(edge => edge.Label == null ? null : NodeShapes.CreateLabel(edge.Label)).ToArray();
        var indices = diagram.Nodes.Select((node, index) => (node.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var margins = new double[boxes.Length];
        var loops = new double[diagram.Edges.Count];
        var loopExtents = new double[boxes.Length];
        var alongSizes = boxes.Select(box => horizontal ? box.Width : box.Height).ToArray();
        for (var index = 0; index < diagram.Edges.Count; index++)
        {
            var edge = diagram.Edges[index];
            if (edge.From != edge.To) continue;
            var node = indices[edge.From];
            loops[index] = 22 + loopExtents[node];
            var label = labels[index];
            var labelExtent = label == null ? 0 : horizontal ? label.Height : label.Width;
            alongSizes[node] = Math.Max(alongSizes[node], label == null ? 0 : horizontal ? label.Width : label.Height);
            loopExtents[node] += 10 + labelExtent / 1.1;
            margins[node] = Math.Max(margins[node], loops[index] * 1.1 + labelExtent + 10);
        }
        var layout = new LayeredLayout().Compute(
            boxes.Select((box, index) => new LayoutNode(horizontal ? alongSizes[index] : box.Width, horizontal ? box.Height : alongSizes[index], margins[index])).ToArray(),
            diagram.Edges.Select((edge, index) => new LayoutEdge(indices[edge.From], indices[edge.To],
                LabelWidth: labels[index]?.Width ?? 0, LabelHeight: labels[index]?.Height ?? 0)).ToArray(),
            diagram.Direction switch
            {
                FlowDirection.BottomToTop => LayoutDirection.BottomToTop,
                FlowDirection.LeftToRight => LayoutDirection.LeftToRight,
                FlowDirection.RightToLeft => LayoutDirection.RightToLeft,
                _ => LayoutDirection.TopToBottom
            });
        return new Scene(diagram, boxes, children, labels, indices, loops, layout);
    }

    private static NodeBox CreateBox(StateNode node, Scene? child, bool horizontal)
    {
        if (node.Kind is StateKind.Start or StateKind.End)
            return new NodeBox(NodeShape.Circle, Array.Empty<string>(), node.Kind == StateKind.Start ? 14 : 20, node.Kind == StateKind.Start ? 14 : 20, node.Kind == StateKind.Start ? 7 : 10);
        if (node.Kind == StateKind.Choice)
            return new NodeBox(NodeShape.Diamond, Array.Empty<string>(), 28, 28, 0);
        if (node.Kind is StateKind.Fork or StateKind.Join)
            return new NodeBox(NodeShape.Rectangle, Array.Empty<string>(), horizontal ? 6 : 56, horizontal ? 56 : 6, 2);
        var box = NodeShapes.CreateNode(new FlowchartNode(node.Id) { Label = node.Label, Shape = NodeShape.Rounded });
        if (child == null) return box;
        return box with
        {
            Width = Math.Max(box.Width, child.Layout.Width + 2 * Padding),
            Height = box.Height + child.Layout.Height + 2 * Padding
        };
    }

    private static void WriteScene(StringBuilder svg, Scene scene, Point origin)
    {
        var horizontal = scene.Diagram.Direction is FlowDirection.LeftToRight or FlowDirection.RightToLeft;
        var axis = horizontal ? new Point(1, 0) : new Point(0, 1);
        var cross = horizontal ? new Point(0, 1) : new Point(1, 0);
        var centers = scene.Layout.NodeCenters.Select(center => center + origin).ToArray();
        var labelCenters = new Point?[scene.Diagram.Edges.Count];
        svg.Append("<g class=\"diagram-edges\">\n");
        for (var index = 0; index < scene.Diagram.Edges.Count; index++)
        {
            var edge = scene.Diagram.Edges[index];
            var from = scene.Indices[edge.From];
            var to = scene.Indices[edge.To];
            if (from == to)
            {
                labelCenters[index] = FlowchartSvgWriter.WriteSelfLoop(svg, edge, scene.Boxes[from], centers[from],
                    scene.Loops[index], scene.Labels[index], axis, cross, horizontal);
                continue;
            }
            var points = scene.Layout.Edges[index].Points.Select(point => point + origin).ToList();
            points[0] = FlowchartSvgWriter.SnapToShape(scene.Boxes[from], centers[from], points[0], axis, cross, horizontal);
            points[^1] = FlowchartSvgWriter.SnapToShape(scene.Boxes[to], centers[to], points[^1], axis, cross, horizontal);
            FlowchartSvgWriter.WriteRoute(svg, edge, points, axis);
            if (scene.Layout.Edges[index].LabelCenter is { } label) labelCenters[index] = label + origin;
        }
        svg.Append("</g>\n<g class=\"diagram-edge-labels\">\n");
        for (var index = 0; index < scene.Labels.Length; index++)
            if (scene.Labels[index] is { } label && labelCenters[index] is { } center) NodeShapes.WriteLabel(svg, label, center);
        svg.Append("</g>\n<g class=\"diagram-nodes\">\n");
        for (var index = 0; index < scene.Boxes.Length; index++)
        {
            var node = scene.Diagram.Nodes[index];
            var box = scene.Boxes[index];
            var center = centers[index];
            var child = scene.Children[index];
            if (child != null)
            {
                WriteComposite(svg, node, box, center, child);
            }
            else if (node.Kind == StateKind.State)
            {
                NodeShapes.WriteNode(svg, box, center, href: node.Href);
            }
            else
            {
                WriteSymbol(svg, node, box, center);
            }
        }
        svg.Append("</g>\n");
    }

    private static void WriteComposite(StringBuilder svg, StateNode node, NodeBox box, Point center, Scene child)
    {
        var left = center.X - box.Width / 2;
        var top = center.Y - box.Height / 2;
        var header = TextMetrics.Measure(box.Lines).Height + 20;
        svg.Append("<g class=\"diagram-state-composite\">");
        var href = SafeHref(node.Href);
        if (href != null)
            svg.Append($"<a class=\"diagram-link\" href=\"{href}\" data-pdf-bounds=\"{F(left)} {F(top)} {F(box.Width)} {F(header)}\">");
        svg.Append($"<rect class=\"diagram-container-shape\" x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" rx=\"12\"/>")
            .Append($"<path class=\"diagram-container-detail\" d=\"M{F(left)} {F(top + header)}h{F(box.Width)}\"/>");
        WriteText(svg, box.Lines, new Point(center.X, top + header / 2), "diagram-container-label");
        if (href != null) svg.Append("</a>");
        WriteScene(svg, child, new Point(left + (box.Width - child.Layout.Width) / 2, top + header + Padding));
        svg.Append("</g>\n");
    }

    private static void WriteSymbol(StringBuilder svg, StateNode node, NodeBox box, Point center)
    {
        var href = SafeHref(node.Href);
        if (href != null)
            svg.Append($"<a class=\"diagram-link\" href=\"{href}\" aria-label=\"{Escape(node.Label)}\" data-pdf-bounds=\"{F(center.X - box.Width / 2)} {F(center.Y - box.Height / 2)} {F(box.Width)} {F(box.Height)}\">");
        svg.Append($"<g class=\"diagram-state-{node.Kind.ToString().ToLowerInvariant()}\"><title>{Escape(node.Kind is StateKind.Start or StateKind.End ? node.Kind.ToString() : node.Label)}</title>");
        if (node.Kind is StateKind.Start or StateKind.End)
        {
            if (node.Kind == StateKind.End)
                svg.Append($"<circle class=\"diagram-node-shape\" cx=\"{F(center.X)}\" cy=\"{F(center.Y)}\" r=\"10\"/>");
            svg.Append($"<circle class=\"diagram-state-symbol\" cx=\"{F(center.X)}\" cy=\"{F(center.Y)}\" r=\"{(node.Kind == StateKind.End ? 6 : 7)}\"/>");
        }
        else if (node.Kind == StateKind.Choice)
        {
            NodeShapes.WriteNode(svg, box, center);
        }
        else
        {
            svg.Append($"<rect class=\"diagram-state-symbol\" x=\"{F(center.X - box.Width / 2)}\" y=\"{F(center.Y - box.Height / 2)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" rx=\"2\"/>");
        }
        svg.Append("</g>");
        if (href != null) svg.Append("</a>");
        svg.Append('\n');
    }

    private sealed record Scene(StateDiagram Diagram, NodeBox[] Boxes, Scene?[] Children, LabelBox?[] Labels,
        Dictionary<string, int> Indices, double[] Loops, LayoutResult Layout);
}
