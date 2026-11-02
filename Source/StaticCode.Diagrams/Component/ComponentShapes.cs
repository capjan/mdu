using System.Text;
using StaticCode.Diagrams.Layout;
using StaticCode.Diagrams.Svg;
using StaticCode.Diagrams.Text;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.Component;

internal sealed record ElementBox(ElementKind Kind, IReadOnlyList<string> Lines, double Width, double Height);

/// <summary>
/// The UML element shapes of component diagrams, drawn with the same classes as flowchart nodes so that they share
/// the theme: a component with its icon, an interface circle, a database cylinder, a 3D node, a cloud, and so on.
/// Containers use the same outlines around their children, with the label in a header.
/// </summary>
internal static class ComponentShapes
{
    public const double ContainerPadding = 14;

    private const double PaddingX = 16;
    private const double PaddingY = 10;
    private const double MinWidth = 72;
    private const double MaxTextWidth = 200;
    private const double InterfaceRadius = 8;
    private const double NodeDepth = 8;
    private const double ContainerNodeDepth = 10;
    private const double CylinderCap = 7;
    private const double FolderTab = 8;
    private const double Fold = 10;
    private const double IconSpace = 16;
    private const double CloudInset = 9;
    private const double HeaderTab = TextMetrics.LineHeight + 6;

    public static ElementBox Create(ComponentElement element)
    {
        var lines = TextMetrics.Wrap(Text(element), MaxTextWidth);
        var (textWidth, textHeight) = TextMetrics.Measure(lines);
        var boxWidth = Math.Max(textWidth + 2 * PaddingX, MinWidth);
        var boxHeight = textHeight + 2 * PaddingY;
        return element.Kind switch
        {
            ElementKind.Interface => new ElementBox(element.Kind, lines, Math.Max(textWidth + 8, 2 * InterfaceRadius + 2), 2 * InterfaceRadius + 4 + textHeight),
            ElementKind.Actor => Actor(lines),
            ElementKind.Component => new ElementBox(element.Kind, lines, Math.Max(textWidth + 2 * PaddingX + IconSpace, MinWidth), boxHeight),
            ElementKind.Database => new ElementBox(element.Kind, lines, boxWidth, boxHeight + 2 * CylinderCap),
            ElementKind.Node => new ElementBox(element.Kind, lines, boxWidth + NodeDepth, boxHeight + NodeDepth),
            ElementKind.Cloud => new ElementBox(element.Kind, lines, boxWidth + 2 * CloudInset, boxHeight + 2 * CloudInset),
            ElementKind.Folder or ElementKind.Package => new ElementBox(element.Kind, lines, boxWidth, boxHeight + FolderTab),
            ElementKind.Queue => new ElementBox(element.Kind, lines, boxWidth + 2 * CylinderCap, boxHeight),
            ElementKind.Storage => new ElementBox(element.Kind, lines, Math.Max(textWidth + boxHeight + 8, MinWidth), boxHeight),
            ElementKind.Note => new ElementBox(element.Kind, lines, Math.Max(textWidth + 2 * 10 + Fold / 2, 40), textHeight + 16),
            _ => new ElementBox(element.Kind, lines, boxWidth, boxHeight)
        };
    }

    private static ElementBox Actor(IReadOnlyList<string> lines)
    {
        var (width, height) = SvgActor.Size(lines);
        return new ElementBox(ElementKind.Actor, lines, width, height);
    }

    public static string Text(ComponentElement element) =>
        element.Stereotype == null ? element.Label : $"«{element.Stereotype}»\n{element.Label}";

    /// <summary>The center of the circle that links of an interface attach to.</summary>
    public static Point InterfaceCenter(ElementBox box, Point center) => new(center.X, center.Y - box.Height / 2 + InterfaceRadius);

    public static double InterfaceRadiusValue => InterfaceRadius;

    public static void WriteElement(StringBuilder svg, ElementBox box, Point center, string? href = null)
    {
        var kind = box.Kind.ToString().ToLowerInvariant();
        var noteClass = box.Kind == ElementKind.Note ? " diagram-note" : string.Empty;
        var safeHref = SafeHref(href);
        var pdfBounds = $"{F(center.X - box.Width / 2)} {F(center.Y - box.Height / 2)} {F(box.Width)} {F(box.Height)}";
        if (safeHref != null) svg.Append($"<a class=\"diagram-link\" href=\"{safeHref}\" data-pdf-bounds=\"{pdfBounds}\">");
        svg.Append($"<g class=\"diagram-node diagram-component-{kind}{noteClass}\">");
        var left = center.X - box.Width / 2;
        var top = center.Y - box.Height / 2;
        var right = left + box.Width;
        var bottom = top + box.Height;
        var textCenter = center;

        switch (box.Kind)
        {
            case ElementKind.Actor:
                SvgActor.Write(svg, box.Lines, center.X, top);
                svg.Append("</g>");
                if (safeHref != null) svg.Append("</a>");
                svg.Append('\n');
                return;
            case ElementKind.Interface:
            {
                var circle = InterfaceCenter(box, center);
                svg.Append($"<circle class=\"diagram-node-shape\" cx=\"{F(circle.X)}\" cy=\"{F(circle.Y)}\" r=\"{F(InterfaceRadius)}\"/>");
                textCenter = new Point(center.X, top + 2 * InterfaceRadius + 4 + box.Lines.Count * TextMetrics.LineHeight / 2);
                break;
            }
            case ElementKind.Component:
                Rect(svg, left, top, box.Width, box.Height, 4);
                ComponentIcon(svg, right - 22, top + 7);
                textCenter = new Point(center.X - IconSpace / 2, center.Y);
                break;
            case ElementKind.Database:
                Cylinder(svg, left, top, right, bottom, CylinderCap);
                textCenter = new Point(center.X, center.Y + CylinderCap / 2);
                break;
            case ElementKind.Node:
                Box3D(svg, left, top, right, bottom, NodeDepth);
                textCenter = new Point(center.X - NodeDepth / 2, center.Y + NodeDepth / 2);
                break;
            case ElementKind.Cloud:
                svg.Append($"<path class=\"diagram-node-shape\" d=\"{CloudPath(left, top, right, bottom)}\"/>");
                break;
            case ElementKind.Folder or ElementKind.Package:
            {
                var tab = Math.Min(box.Width * 0.4, 44);
                svg.Append($"<path class=\"diagram-node-shape\" d=\"M{F(left)} {F(bottom)} V{F(top)} H{F(left + tab)} L{F(left + tab + FolderTab)} {F(top + FolderTab)} H{F(right)} V{F(bottom)} Z\"/>");
                svg.Append($"<path class=\"diagram-node-detail\" d=\"M{F(left)} {F(top + FolderTab)} H{F(left + tab + FolderTab)}\"/>");
                textCenter = new Point(center.X, center.Y + FolderTab / 2);
                break;
            }
            case ElementKind.Queue:
            {
                var radiusY = box.Height / 2;
                svg.Append($"<path class=\"diagram-node-shape\" d=\"M{F(left + CylinderCap)} {F(top)} H{F(right - CylinderCap)} A{F(CylinderCap)} {F(radiusY)} 0 0 1 {F(right - CylinderCap)} {F(bottom)}")
                    .Append($" H{F(left + CylinderCap)} A{F(CylinderCap)} {F(radiusY)} 0 0 1 {F(left + CylinderCap)} {F(top)} Z\"/>");
                svg.Append($"<path class=\"diagram-node-detail\" d=\"M{F(right - CylinderCap)} {F(top)} A{F(CylinderCap)} {F(radiusY)} 0 0 0 {F(right - CylinderCap)} {F(bottom)}\"/>");
                textCenter = new Point(center.X - CylinderCap / 2, center.Y);
                break;
            }
            case ElementKind.Storage:
                Rect(svg, left, top, box.Width, box.Height, box.Height / 2);
                break;
            case ElementKind.Note or ElementKind.Artifact or ElementKind.File:
            {
                var shapeClass = box.Kind == ElementKind.Note ? "diagram-note-shape" : "diagram-node-shape";
                svg.Append($"<path class=\"{shapeClass}\" d=\"M{F(left)} {F(top)} H{F(right - Fold)} L{F(right)} {F(top + Fold)} V{F(bottom)} H{F(left)} Z\"/>");
                svg.Append($"<path class=\"diagram-node-detail\" d=\"M{F(right - Fold)} {F(top)} V{F(top + Fold)} H{F(right)}\"/>");
                textCenter = new Point(center.X - Fold / 4, center.Y);
                break;
            }
            default:
                Rect(svg, left, top, box.Width, box.Height, 4);
                break;
        }

        WriteText(svg, box.Lines, textCenter, box.Kind == ElementKind.Note ? "diagram-note-text" : "diagram-node-text");
        svg.Append("</g>");
        if (safeHref != null) svg.Append("</a>");
        svg.Append('\n');
    }

    /// <summary>Space between the top of a container and its content, for the label.</summary>
    public static double HeaderHeight(ComponentElement element)
    {
        var extraLines = (TextMetrics.Wrap(Text(element), double.MaxValue).Count - 1) * TextMetrics.LineHeight;
        return extraLines + element.Kind switch
        {
            ElementKind.Node => HeaderTab + 8 + ContainerNodeDepth,
            ElementKind.Cloud => HeaderTab + 8 + CloudInset,
            ElementKind.Database => HeaderTab + 8 + 2 * 10,
            _ => HeaderTab + 8
        };
    }

    /// <summary>The smallest width that fits the container's label.</summary>
    /// <summary>Where a container's label sits: its left edge and size, measured from the container's top.</summary>
    public static (double Left, double Width, double Height) LabelArea(ComponentElement element, double left, double width)
    {
        var label = TextMetrics.Wrap(Text(element), double.MaxValue);
        var labelWidth = TextMetrics.Measure(label).Width + 20;
        var centered = element.Kind is ElementKind.Cloud or ElementKind.Database;
        return (centered ? left + (width - labelWidth) / 2 : left, labelWidth, HeaderHeight(element) - 8);
    }

    public static double MinContainerWidth(ComponentElement element) =>
        TextMetrics.Width(Text(element).Split('\n')[^1]) + 2 * ContainerPadding + 24;

    public static void WriteContainer(StringBuilder svg, ComponentElement element, double left, double top, double width, double height)
    {
        var right = left + width;
        var bottom = top + height;
        var kind = element.Kind.ToString().ToLowerInvariant();
        var label = TextMetrics.Wrap(Text(element), double.MaxValue);
        var labelWidth = TextMetrics.Measure(label).Width;
        var labelClass = "diagram-container-label";
        var safeHref = SafeHref(element.Href);
        var pdfBounds = $"{F(left)} {F(top)} {F(width)} {F(height)}";
        if (safeHref != null) svg.Append($"<a class=\"diagram-link\" href=\"{safeHref}\" data-pdf-bounds=\"{pdfBounds}\">");
        svg.Append($"<g class=\"diagram-container diagram-container-{kind}\">");

        switch (element.Kind)
        {
            case ElementKind.Package or ElementKind.Folder:
            {
                var tab = labelWidth + 20;
                var tabHeight = HeaderTab + (label.Count - 1) * TextMetrics.LineHeight;
                svg.Append($"<path class=\"diagram-container-shape\" d=\"M{F(left)} {F(bottom)} V{F(top)} H{F(left + tab)} V{F(top + tabHeight)} H{F(right)} V{F(bottom)} Z\"/>")
                    .Append($"<path class=\"diagram-container-detail\" d=\"M{F(left)} {F(top + tabHeight)} H{F(left + tab)}\"/>");
                WriteText(svg, label, new Point(left + 10, top + tabHeight / 2), labelClass, TextAlignment.Start);
                break;
            }
            case ElementKind.Frame:
            {
                var tab = labelWidth + 16;
                var tabHeight = HeaderTab + (label.Count - 1) * TextMetrics.LineHeight;
                svg.Append($"<rect class=\"diagram-container-shape\" x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(width)}\" height=\"{F(height)}\" rx=\"4\"/>")
                    .Append($"<path class=\"diagram-container-tab\" d=\"M{F(left)} {F(top + 4)} Q{F(left)} {F(top)} {F(left + 4)} {F(top)} H{F(left + tab + 6)}")
                    .Append($" V{F(top + tabHeight - 8)} L{F(left + tab)} {F(top + tabHeight)} H{F(left)} Z\"/>");
                WriteText(svg, label, new Point(left + 8, top + tabHeight / 2), labelClass, TextAlignment.Start);
                break;
            }
            case ElementKind.Node:
                Box3D(svg, left, top, right, bottom, ContainerNodeDepth, "diagram-container-shape", "diagram-container-detail");
                WriteText(svg, label, new Point(left + 10, top + ContainerNodeDepth + HeaderTab / 2 + 2), labelClass, TextAlignment.Start);
                break;
            case ElementKind.Cloud:
                svg.Append($"<path class=\"diagram-container-shape\" d=\"{CloudPath(left, top, right, bottom)}\"/>");
                WriteText(svg, label, new Point((left + right) / 2, top + CloudInset + HeaderTab / 2 + 2), labelClass);
                break;
            case ElementKind.Database:
                Cylinder(svg, left, top, right, bottom, 10, "diagram-container-shape", "diagram-container-detail");
                WriteText(svg, label, new Point((left + right) / 2, top + 20 + HeaderTab / 2), labelClass);
                break;
            default:
                svg.Append($"<rect class=\"diagram-container-shape\" x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(width)}\" height=\"{F(height)}\" rx=\"4\"/>");
                if (element.Kind == ElementKind.Component) ComponentIcon(svg, right - 22, top + 7);
                WriteText(svg, label, new Point(left + 10, top + HeaderTab / 2 + 2), labelClass, TextAlignment.Start);
                break;
        }

        svg.Append("</g>");
        if (safeHref != null) svg.Append("</a>");
        svg.Append('\n');
    }

    private static void Rect(StringBuilder svg, double left, double top, double width, double height, double radius) =>
        svg.Append($"<rect class=\"diagram-node-shape\" x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(width)}\" height=\"{F(height)}\" rx=\"{F(radius)}\"/>");

    /// <summary>The UML component icon: a small box with two tabs on its left side.</summary>
    private static void ComponentIcon(StringBuilder svg, double left, double top)
    {
        svg.Append($"<rect class=\"diagram-node-icon\" x=\"{F(left)}\" y=\"{F(top)}\" width=\"12\" height=\"14\" rx=\"1\"/>")
            .Append($"<rect class=\"diagram-node-icon\" x=\"{F(left - 3)}\" y=\"{F(top + 3)}\" width=\"6\" height=\"3\"/>")
            .Append($"<rect class=\"diagram-node-icon\" x=\"{F(left - 3)}\" y=\"{F(top + 8)}\" width=\"6\" height=\"3\"/>");
    }

    private static void Cylinder(
        StringBuilder svg, double left, double top, double right, double bottom, double cap,
        string shapeClass = "diagram-node-shape", string detailClass = "diagram-node-detail")
    {
        var radiusX = (right - left) / 2;
        svg.Append($"<path class=\"{shapeClass}\" d=\"M{F(left)} {F(top + cap)} A{F(radiusX)} {F(cap)} 0 0 1 {F(right)} {F(top + cap)}")
            .Append($" V{F(bottom - cap)} A{F(radiusX)} {F(cap)} 0 0 1 {F(left)} {F(bottom - cap)} Z\"/>")
            .Append($"<path class=\"{detailClass}\" d=\"M{F(left)} {F(top + cap)} A{F(radiusX)} {F(cap)} 0 0 0 {F(right)} {F(top + cap)}\"/>");
    }

    private static void Box3D(
        StringBuilder svg, double left, double top, double right, double bottom, double depth,
        string shapeClass = "diagram-node-shape", string detailClass = "diagram-node-detail")
    {
        svg.Append($"<path class=\"{shapeClass}\" d=\"M{F(left)} {F(top + depth)} L{F(left + depth)} {F(top)} H{F(right)} V{F(bottom - depth)} L{F(right - depth)} {F(bottom)} H{F(left)} Z\"/>")
            .Append($"<path class=\"{detailClass}\" d=\"M{F(left)} {F(top + depth)} H{F(right - depth)} V{F(bottom)} M{F(right - depth)} {F(top + depth)} L{F(right)} {F(top)}\"/>");
    }

    /// <summary>A cloud outline: arcs that bulge outward along the edges of a rectangle inset by <see cref="CloudInset"/>.</summary>
    private static string CloudPath(double left, double top, double right, double bottom)
    {
        var x0 = left + CloudInset;
        var y0 = top + CloudInset;
        var x1 = right - CloudInset;
        var y1 = bottom - CloudInset;
        var path = new StringBuilder($"M{F(x0)} {F(y0)}");

        void Edge(Point from, Point to)
        {
            var length = (to - from).Length;
            var count = Math.Max(2, (int)Math.Round(length / 26));
            var step = (to - from) * (1.0 / count);
            var radius = step.Length * 0.62;
            for (var index = 1; index <= count; index++)
            {
                var point = from + step * index;
                path.Append($" A{F(radius)} {F(radius)} 0 0 1 {F(point.X)} {F(point.Y)}");
            }
        }

        Edge(new Point(x0, y0), new Point(x1, y0));
        Edge(new Point(x1, y0), new Point(x1, y1));
        Edge(new Point(x1, y1), new Point(x0, y1));
        Edge(new Point(x0, y1), new Point(x0, y0));
        return path.Append(" Z").ToString();
    }
}
