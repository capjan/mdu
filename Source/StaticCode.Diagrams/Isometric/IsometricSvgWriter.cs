using System.Globalization;
using System.Text;
using StaticCode.Diagrams.Layout;
using StaticCode.Diagrams.Svg;
using StaticCode.Diagrams.Text;
using StaticCode.Icons;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.Isometric;

/// <summary>
/// Writes an isometric diagram as HTML: phase controls followed by inline SVG. World coordinates lie on the ground
/// plane, x toward the front right and y toward the front left, and are projected with the 30° isometric angles.
/// Zones and standalone nodes sit in a coarse grid of columns and rows; the nodes of a zone fill a grid of cells on
/// its platform. The SVG carries only CSS classes, so the stylesheet themes it. Every element carries the phase in
/// which it appears as <c>--iso-phase</c>; the checked phase control sets <c>--iso-current</c>, and the stylesheet
/// fades everything that belongs to a later phase.
/// </summary>
internal static class IsometricSvgWriter
{
    private const double Pitch = 80;
    private const double ZoneGap = 56;
    private const double PlatformHeight = 6;
    private const double ZonePadding = 16;
    private const double Cos = 0.86602540378;
    private const double Sin = 0.5;
    private const double CanvasPadding = 8;
    private const double LabelGap = 2;
    private const double LabelHeight = 18;
    private const double LabelPadding = 6;
    private const double LabelFontScale = 12.0 / 14;
    private const double TitleInset = 10;
    private const double CornerRadius = 12;
    private const double FlowSpeed = 70;
    private const double ParticleSpacing = 150;

    private readonly record struct ShapeSize(double Footprint, double Height, double IconSize);

    private sealed class Item
    {
        public IsometricZone? Zone { get; init; }
        public IsometricNode? Node { get; init; }
        public double Width { get; init; }
        public double Depth { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed record PlacedNode(IsometricNode Node, double X, double Y, double Base, ShapeSize Size, string Tone)
    {
        public double Top => Base + Size.Height;
    }

    public static string Write(IsometricDiagram diagram)
    {
        var cells = new Dictionary<IsometricNode, GridCell>();
        var items = new List<Item>();
        foreach (var zone in diagram.Zones)
        {
            var zoneCells = AssignCells(zone.Nodes.Select(node => node.Cell).ToList(), zone.Columns);
            for (var index = 0; index < zone.Nodes.Count; index++) cells[zone.Nodes[index]] = zoneCells[index];
            var columns = zoneCells.Select(cell => cell.Column + 1).DefaultIfEmpty(1).Max();
            var rows = zoneCells.Select(cell => cell.Row + 1).DefaultIfEmpty(1).Max();
            items.Add(new Item { Zone = zone, Width = columns * Pitch + 2 * ZonePadding, Depth = rows * Pitch + 2 * ZonePadding });
        }

        foreach (var node in diagram.Nodes.Where(node => node.Zone == null))
            items.Add(new Item { Node = node, Width = Pitch, Depth = Pitch });

        // Zones and standalone nodes keep their declaration order, so a standalone node after a zone stays after it.
        var order = diagram.Nodes.Select(node => node.Zone as object ?? node).Distinct().ToList();
        items = items.OrderBy(item => order.IndexOf(item.Zone as object ?? item.Node!)).ToList();
        PlaceItems(items);

        var placed = new List<PlacedNode>();
        foreach (var item in items)
        {
            if (item.Zone is { } zone)
            {
                foreach (var node in zone.Nodes)
                {
                    var cell = cells[node];
                    placed.Add(new PlacedNode(node, item.X + ZonePadding + (cell.Column + 0.5) * Pitch, item.Y + ZonePadding + (cell.Row + 0.5) * Pitch,
                        PlatformHeight, SizeOf(node.Shape), node.Tone ?? zone.Tone ?? "gray"));
                }
            }
            else
            {
                var node = item.Node!;
                placed.Add(new PlacedNode(node, item.X + item.Width / 2, item.Y + item.Depth / 2, 0, SizeOf(node.Shape), node.Tone ?? "gray"));
            }
        }

        var byId = placed.ToDictionary(node => node.Node.Id);
        var bounds = new Bounds();
        var platforms = new StringBuilder();
        var nodes = new StringBuilder();
        var edges = new StringBuilder();
        var labels = new StringBuilder();
        var labelBoxes = new List<LabelBox>();

        foreach (var item in items.Where(item => item.Zone != null).OrderBy(item => item.X + item.Y))
            WritePlatform(platforms, labels, item, bounds, labelBoxes);

        foreach (var node in placed.OrderBy(node => node.X + node.Y).ThenBy(node => node.X))
            WriteNode(nodes, labels, node, bounds, labelBoxes);

        foreach (var edge in diagram.Edges)
            WriteEdge(edges, labels, edge, byId[edge.From], byId[edge.To], placed, bounds, labelBoxes);

        var width = bounds.MaxX - bounds.MinX + 2 * CanvasPadding;
        var height = bounds.MaxY - bounds.MinY + 2 * CanvasPadding;
        var html = new StringBuilder();
        html.Append("<div class=\"iso\">\n");
        if (diagram.Phases.Count > 0) WriteControls(html, diagram);
        html.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-isometric\"")
            .Append($" viewBox=\"{F(bounds.MinX - CanvasPadding)} {F(bounds.MinY - CanvasPadding)} {F(width)} {F(height)}\"")
            .Append($" width=\"{F(width)}\" height=\"{F(height)}\"")
            .Append($" font-size=\"{F(TextMetrics.FontSize)}\" role=\"img\" aria-label=\"{Escape(diagram.Title ?? "Isometric diagram")}\">\n")
            .Append("<g class=\"iso-platforms\">\n").Append(platforms)
            .Append("</g>\n<g class=\"iso-nodes\">\n").Append(nodes)
            .Append("</g>\n<g class=\"iso-edges\">\n").Append(edges)
            .Append("</g>\n<g class=\"iso-labels\">\n").Append(labels)
            .Append("</g>\n</svg>\n</div>");
        return html.ToString();
    }

    private static ShapeSize SizeOf(IsometricShape shape) => shape switch
    {
        IsometricShape.Tall => new ShapeSize(34, 40, 18),
        IsometricShape.Large => new ShapeSize(46, 32, 24),
        IsometricShape.Disc => new ShapeSize(40, 8, 18),
        _ => new ShapeSize(34, 24, 18)
    };

    private static Point Project(double x, double y, double z) => new((x - y) * Cos, (x + y) * Sin - z);

    /// <summary>Keeps fixed cells and fills the remaining items row by row into the free cells.</summary>
    private static List<GridCell> AssignCells(IReadOnlyList<GridCell?> fixedCells, int? columns)
    {
        var width = columns ?? Math.Max(1, (int)Math.Ceiling(Math.Sqrt(fixedCells.Count)));
        var used = fixedCells.Where(cell => cell.HasValue).Select(cell => cell!.Value).ToHashSet();
        var result = new List<GridCell>();
        var next = 0;
        foreach (var cell in fixedCells)
        {
            if (cell is { } fixedCell)
            {
                result.Add(fixedCell);
                continue;
            }

            GridCell free;
            do free = new GridCell(next % width, next++ / width);
            while (used.Contains(free));
            used.Add(free);
            result.Add(free);
        }

        return result;
    }

    /// <summary>Sizes every grid column and row to its largest item and centers each item in its cell.</summary>
    private static void PlaceItems(List<Item> items)
    {
        var cells = AssignCells(items.Select(item => item.Zone?.Cell ?? item.Node?.Cell).ToList(), null);
        var columnWidths = new double[cells.Max(cell => cell.Column) + 1];
        var rowDepths = new double[cells.Max(cell => cell.Row) + 1];
        for (var index = 0; index < items.Count; index++)
        {
            columnWidths[cells[index].Column] = Math.Max(columnWidths[cells[index].Column], items[index].Width);
            rowDepths[cells[index].Row] = Math.Max(rowDepths[cells[index].Row], items[index].Depth);
        }

        for (var index = 0; index < items.Count; index++)
        {
            var (column, row) = (cells[index].Column, cells[index].Row);
            var x = columnWidths.Take(column).Sum() + column * ZoneGap;
            var y = rowDepths.Take(row).Sum() + row * ZoneGap;
            items[index].X = x + (columnWidths[column] - items[index].Width) / 2;
            items[index].Y = y + (rowDepths[row] - items[index].Depth) / 2;
        }
    }

    private static string Phased(string classes, int phase) =>
        $"<g class=\"iso-phased {classes}\" style=\"--iso-phase: {phase.ToString(CultureInfo.InvariantCulture)}\">";

    private static void WritePlatform(StringBuilder svg, StringBuilder labels, Item item, Bounds bounds, List<LabelBox> labelBoxes)
    {
        var zone = item.Zone!;
        var (x0, y0, x1, y1) = (item.X, item.Y, item.X + item.Width, item.Y + item.Depth);
        svg.Append(Phased($"iso-zone iso-tone-{zone.Tone ?? "gray"}", zone.Phase));
        WriteBox(svg, x0, y0, x1, y1, 0, PlatformHeight, "iso-platform", bounds);

        svg.Append("<g class=\"iso-grid\">");
        for (var x = x0 + ZonePadding; x < x1 - 1; x += Pitch / 2) WriteLine(svg, Project(x, y0, PlatformHeight), Project(x, y1, PlatformHeight));
        for (var y = y0 + ZonePadding; y < y1 - 1; y += Pitch / 2) WriteLine(svg, Project(x0, y, PlatformHeight), Project(x1, y, PlatformHeight));
        svg.Append("</g></g>\n");

        // The title hangs below the front-left edge like lettering on the platform's side, in the gap before the next row.
        if (zone.Title == null && zone.Subtitle == null) return;
        var corner = Project(x0, y1, 0);
        labels.Append(Phased("iso-zone-heading", zone.Phase))
            .Append($"<g transform=\"matrix({F(Cos)} {F(Sin)} 0 1 {F(corner.X)} {F(corner.Y)})\">");
        // Labels of the front row can reach below the edge, so the lettering starts lower.
        var baseline = 10.0;
        var direction = new Point(Cos, Sin);
        foreach (var (text, cssClass, size) in new[] { (zone.Title, "iso-zone-title", 13.0), (zone.Subtitle, "iso-zone-subtitle", 11.0) })
        {
            if (text == null) continue;
            baseline += size + 3;
            labels.Append($"<text class=\"{cssClass}\" x=\"{F(TitleInset)}\" y=\"{F(baseline)}\">{Escape(text)}</text>");
            var length = TextMetrics.Width(text) * size / TextMetrics.FontSize * 1.08;
            bounds.Include(corner + direction * (TitleInset + length) + new Point(0, baseline), 0, 0);

            // Link labels avoid the slanted lettering, which a row of small boxes approximates.
            for (var offset = 0.0; offset < length; offset += 20)
                labelBoxes.Add(new LabelBox(corner + direction * (TitleInset + offset + 10) + new Point(0, baseline - size * 0.35), 18, size + 10));
        }

        labels.Append("</g></g>\n");
        bounds.Include(corner + new Point(0, baseline + 4), 0, 0);
    }

    private static void WriteNode(StringBuilder svg, StringBuilder labels, PlacedNode placed, Bounds bounds, List<LabelBox> labelBoxes)
    {
        var node = placed.Node;
        var half = placed.Size.Footprint / 2;
        svg.Append(Phased($"iso-node iso-tone-{placed.Tone}", node.Phase));
        double bottom;
        if (node.Shape == IsometricShape.Disc)
        {
            var top = Project(placed.X, placed.Y, placed.Top);
            var radiusX = half * Math.Sqrt(2) * Cos;
            var radiusY = half * Math.Sqrt(2) * Sin;
            var height = placed.Size.Height;
            svg.Append($"<path class=\"iso-face-left\" d=\"M{F(top.X - radiusX)} {F(top.Y)} L{F(top.X - radiusX)} {F(top.Y + height)}")
                .Append($" A{F(radiusX)} {F(radiusY)} 0 0 0 {F(top.X + radiusX)} {F(top.Y + height)} L{F(top.X + radiusX)} {F(top.Y)} Z\"/>")
                .Append($"<ellipse class=\"iso-face-top\" cx=\"{F(top.X)}\" cy=\"{F(top.Y)}\" rx=\"{F(radiusX)}\" ry=\"{F(radiusY)}\"/>");
            bounds.Include(top, 2 * radiusX, 2 * radiusY);
            bottom = top.Y + height + radiusY;
        }
        else
        {
            WriteBox(svg, placed.X - half, placed.Y - half, placed.X + half, placed.Y + half, placed.Base, placed.Top, "iso-block", bounds);
            bottom = Project(placed.X + half, placed.Y + half, placed.Base).Y;
        }

        if (node.Icon != null) WriteIcon(svg, node.Icon, Project(placed.X, placed.Y, placed.Top), placed.Size.IconSize);
        svg.Append("</g>\n");

        var labelCenter = new Point(Project(placed.X, placed.Y, 0).X, bottom + LabelGap + LabelHeight / 2);
        var box = new LabelBox(labelCenter, LabelWidth(node.Label), LabelHeight);
        labelBoxes.Add(box);
        labels.Append(Phased("iso-node-label", node.Phase));
        WriteLabel(labels, node.Label, box, bounds);
        labels.Append("</g>\n");
    }

    private static void WriteBox(StringBuilder svg, double x0, double y0, double x1, double y1, double z0, double z1, string shapeClass, Bounds bounds)
    {
        Point[] top = [Project(x0, y0, z1), Project(x1, y0, z1), Project(x1, y1, z1), Project(x0, y1, z1)];
        Point[] left = [Project(x0, y1, z0), Project(x1, y1, z0), Project(x1, y1, z1), Project(x0, y1, z1)];
        Point[] right = [Project(x1, y0, z0), Project(x1, y1, z0), Project(x1, y1, z1), Project(x1, y0, z1)];
        svg.Append($"<g class=\"{shapeClass}\">");
        WritePolygon(svg, "iso-face-left", left);
        WritePolygon(svg, "iso-face-right", right);
        WritePolygon(svg, "iso-face-top", top);
        svg.Append("</g>");
        foreach (var point in top.Concat(left).Concat(right)) bounds.Include(point, 0, 0);
    }

    private static void WritePolygon(StringBuilder svg, string cssClass, IEnumerable<Point> points) =>
        svg.Append($"<polygon class=\"{cssClass}\" points=\"{string.Join(' ', points.Select(point => $"{F(point.X)},{F(point.Y)}"))}\"/>");

    private static void WriteLine(StringBuilder svg, Point from, Point to) =>
        svg.Append($"<line x1=\"{F(from.X)}\" y1=\"{F(from.Y)}\" x2=\"{F(to.X)}\" y2=\"{F(to.Y)}\"/>");

    /// <summary>Draws the icon upright on the center of the top face. Icons with a dark variant get one group per theme.</summary>
    private static void WriteIcon(StringBuilder svg, string shortcode, Point center, double size)
    {
        if (!IconCatalog.TryGetIcon(shortcode, out var icon))
            throw new DiagramSyntaxException($"Unknown icon '{shortcode}'.", 1);

        svg.Append("<g class=\"iso-icon\">");
        WriteGlyph(svg, icon.Glyph, icon.DarkGlyph == null ? null : "iso-icon-light", center, size);
        if (icon.DarkGlyph != null) WriteGlyph(svg, icon.DarkGlyph, "iso-icon-dark", center, size);
        svg.Append("</g>");
    }

    private static void WriteGlyph(StringBuilder svg, IconGlyph glyph, string? cssClass, Point center, double size)
    {
        var scale = size / Math.Max(glyph.Width, glyph.Height);
        var x = center.X - glyph.Width * scale / 2 - glyph.Left * scale;
        var y = center.Y - glyph.Height * scale / 2 - glyph.Top * scale;
        var classAttribute = cssClass == null ? string.Empty : $" class=\"{cssClass}\"";
        svg.Append($"<g{classAttribute} transform=\"translate({F(x)} {F(y)}) scale({scale.ToString("0.####", CultureInfo.InvariantCulture)})\">{glyph.Body}</g>");
    }

    /// <summary>
    /// Links run from the top of one node to the top of the other along the two ground axes, with one rounded bend.
    /// Particles move along the link in its direction.
    /// </summary>
    private static void WriteEdge(StringBuilder svg, StringBuilder labels, IsometricEdge edge, PlacedNode from, PlacedNode to,
        IReadOnlyList<PlacedNode> nodes, Bounds bounds, List<LabelBox> labelBoxes)
    {
        var start = Project(from.X, from.Y, from.Top);
        var end = Project(to.X, to.Y, to.Top);
        Point[] points;
        if (Math.Abs(from.X - to.X) < 1 || Math.Abs(from.Y - to.Y) < 1)
        {
            points = [start, end];
        }
        else
        {
            // Bend where the link passes over fewer other nodes; along x first when both are equal.
            var others = nodes.Where(node => node != from && node != to).ToList();
            var xFirst = new Point(to.X, from.Y);
            var yFirst = new Point(from.X, to.Y);
            var corner = Crossings(others, from, xFirst, to) <= Crossings(others, from, yFirst, to) ? xFirst : yFirst;
            points = [start, Project(corner.X, corner.Y, from.Top), end];
        }

        var path = new StringBuilder($"M{F(start.X)} {F(start.Y)}");
        if (points.Length == 3)
        {
            var corner = points[1];
            var radius = Math.Min(CornerRadius, Math.Min((corner - start).Length, (end - corner).Length) / 2);
            var before = corner - (corner - start).Normalized * radius;
            var after = corner + (end - corner).Normalized * radius;
            path.Append($" L{F(before.X)} {F(before.Y)} Q{F(corner.X)} {F(corner.Y)} {F(after.X)} {F(after.Y)}");
        }

        path.Append($" L{F(end.X)} {F(end.Y)}");
        var d = path.ToString();

        var style = edge.Style switch
        {
            IsometricEdgeStyle.Dashed => "iso-edge-dashed",
            IsometricEdgeStyle.Thick => "iso-edge-thick",
            _ => "iso-edge-solid"
        };
        svg.Append(Phased($"iso-edge {style} iso-tone-{from.Tone}", edge.Phase))
            .Append($"<path class=\"iso-edge-line\" d=\"{d}\"/>");

        // End points and particles disappear completely in later phases, since several faded ones would add up.
        var length = points.Zip(points.Skip(1), (a, b) => (b - a).Length).Sum();
        var duration = Math.Max(1.6, length / FlowSpeed);
        var count = Math.Max(1, (int)Math.Round(length / ParticleSpacing));
        svg.Append("<g class=\"iso-flow\">")
            .Append($"<circle class=\"iso-edge-end\" cx=\"{F(start.X)}\" cy=\"{F(start.Y)}\" r=\"2.5\"/>")
            .Append($"<circle class=\"iso-edge-end\" cx=\"{F(end.X)}\" cy=\"{F(end.Y)}\" r=\"2.5\"/>");
        for (var index = 0; index < count; index++)
            svg.Append("<circle class=\"iso-particle\" r=\"3\">")
                .Append($"<animateMotion dur=\"{F(duration)}s\" begin=\"{F(-index * duration / count)}s\" repeatCount=\"indefinite\" path=\"{d}\"/>")
                .Append("</circle>");
        svg.Append("</g></g>\n");
        foreach (var point in points) bounds.Include(point, 0, 0);

        if (edge.Label == null) return;

        // The label prefers the middle of the longest segment and slides along the link until it clears other labels.
        var width = LabelWidth(edge.Label);
        var segments = points.Zip(points.Skip(1)).OrderByDescending(segment => (segment.Second - segment.First).Length).ToList();
        var candidates = segments
            .SelectMany(segment => new[] { 0.5, 0.35, 0.65, 0.2, 0.8 }.Select(t => segment.First + (segment.Second - segment.First) * t))
            .Select(center => new LabelBox(center, width, LabelHeight))
            .ToList();
        var box = candidates.FirstOrDefault(candidate => !labelBoxes.Any(candidate.Overlaps)) ?? candidates[0];
        labelBoxes.Add(box);
        labels.Append(Phased("iso-edge-label", edge.Phase));
        WriteLabel(labels, edge.Label, box, bounds);
        labels.Append("</g>\n");
    }

    /// <summary>Counts the nodes whose footprint an axis-aligned route through <paramref name="corner"/> crosses.</summary>
    private static int Crossings(IEnumerable<PlacedNode> nodes, PlacedNode from, Point corner, PlacedNode to) =>
        nodes.Count(node =>
        {
            var half = node.Size.Footprint / 2 + 4;
            bool Crosses(double x0, double y0, double x1, double y1) =>
                Math.Min(x0, x1) - half < node.X && node.X < Math.Max(x0, x1) + half &&
                Math.Min(y0, y1) - half < node.Y && node.Y < Math.Max(y0, y1) + half;
            return Crosses(from.X, from.Y, corner.X, corner.Y) || Crosses(corner.X, corner.Y, to.X, to.Y);
        });

    private static double LabelWidth(string text) => TextMetrics.Width(text) * LabelFontScale + 2 * LabelPadding;

    private static void WriteLabel(StringBuilder svg, string text, LabelBox box, Bounds bounds)
    {
        svg.Append($"<rect class=\"iso-label-background\" x=\"{F(box.Center.X - box.Width / 2)}\" y=\"{F(box.Center.Y - box.Height / 2)}\"")
            .Append($" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" rx=\"6\"/>");
        WriteText(svg, new[] { text }, box.Center, "iso-label");
        bounds.Include(box.Center, box.Width, box.Height);
    }

    private sealed record LabelBox(Point Center, double Width, double Height)
    {
        public bool Overlaps(LabelBox other) =>
            Math.Abs(Center.X - other.Center.X) < (Width + other.Width) / 2 + 2 &&
            Math.Abs(Center.Y - other.Center.Y) < (Height + other.Height) / 2 + 2;
    }

    private static void WriteControls(StringBuilder html, IsometricDiagram diagram)
    {
        // The radio group name only has to be unique on the page; a hash of the content keeps the output stable.
        var name = $"iso-{StableHash(string.Join('\n', diagram.Phases) + diagram.Nodes.Count + diagram.Edges.Count):x8}";
        html.Append("<div class=\"iso-controls\" role=\"group\" aria-label=\"Phases\">\n")
            .Append("<button type=\"button\" class=\"iso-play\" hidden=\"\"><span class=\"iso-play-icon\" aria-hidden=\"true\"></span><span class=\"iso-play-text\">Play</span></button>\n");
        for (var index = 0; index < diagram.Phases.Count; index++)
        {
            var phase = (index + 1).ToString(CultureInfo.InvariantCulture);
            var title = Escape(diagram.Phases[index]);
            html.Append($"<label class=\"iso-step\" title=\"{title}\"><input type=\"radio\" name=\"{name}\" data-phase=\"{phase}\" aria-label=\"Phase {phase}: {title}\"")
                .Append(index == diagram.Phases.Count - 1 ? " checked=\"\"" : string.Empty)
                .Append($"/><span class=\"iso-step-number\">{phase}</span><span class=\"iso-step-title\">{title}</span></label>\n");
        }

        html.Append("</div>\n");
    }

    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text) hash = (hash ^ character) * 16777619u;
        return hash;
    }

    private sealed class Bounds
    {
        public double MinX { get; private set; } = double.PositiveInfinity;
        public double MinY { get; private set; } = double.PositiveInfinity;
        public double MaxX { get; private set; } = double.NegativeInfinity;
        public double MaxY { get; private set; } = double.NegativeInfinity;

        public void Include(Point center, double width, double height)
        {
            MinX = Math.Min(MinX, center.X - width / 2);
            MinY = Math.Min(MinY, center.Y - height / 2);
            MaxX = Math.Max(MaxX, center.X + width / 2);
            MaxY = Math.Max(MaxY, center.Y + height / 2);
        }
    }
}
