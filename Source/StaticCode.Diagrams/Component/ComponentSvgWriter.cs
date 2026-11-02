using System.Text;
using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.Layout;
using StaticCode.Diagrams.Svg;
using StaticCode.Diagrams.Text;
using static StaticCode.Diagrams.Svg.SvgText;

namespace StaticCode.Diagrams.Component;

/// <summary>
/// Lays out and writes a component diagram. Containers are laid out from the inside out: each container's children
/// get their own layered layout, and the container then takes part in its parent's layout as one box. A link that
/// crosses a container's border enters or leaves through a port node pinned to the first or last rank inside, and the
/// parent routes the link to that port's position on the border, so the pieces join into one route.
/// Further passes pull the ports toward the far ends of their links, which lines up elements across containers.
/// When everything is placed, each link takes the shortest way around the elements and labels between its ends.
/// </summary>
internal sealed class ComponentSvgWriter
{
    private const double CanvasPadding = 4;
    private const double TitleHeight = TextMetrics.LineHeight + 14;

    // The layout's ranks: with direct links a rank gap is half of this.
    private const double RankSeparation = 120;

    // Layouts after the first know where the other ends of the links that leave a container are.
    private const int LayoutPasses = 3;

    // A link looks for its way among this many obstacles nearest to it, which bounds the search in very large diagrams.
    private const int MaxDetourObstacles = 48;

    // Free space that a link keeps around elements and container labels.
    private const double ObstacleMargin = 6;
    private const double PortSpacing = 30;
    private const double CornerRadius = 16;
    private const double EdgeLabelMargin = 2;

    // Link ends that move beside a container's label stay this far from the corner of their element.
    private const double PortInset = 8;

    // A last segment shorter than this along the axis, and much flatter than steep, reads as a hook.
    private const double MinApproach = 8;

    // Link ends stay within this share of a side around its center, and lean toward the other end by the second one.
    private const double PortReach = 0.35;
    private const double PortLean = 0.25;

    private readonly ComponentDiagram _diagram;
    private readonly Dictionary<string, ComponentElement> _elements;
    private readonly Dictionary<string, ElementBox> _boxes;
    private readonly List<RoutedLink> _links;
    private readonly Dictionary<string, ContainerLayout> _containers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> _centers = new(StringComparer.Ordinal);
    private readonly bool _horizontal;
    private readonly Point _axis;
    private readonly Point _cross;
    private ContainerLayout? _root;

    // Measured once, when routing starts and the containers no longer move.
    private readonly Dictionary<string, Bounds> _labelBounds = new(StringComparer.Ordinal);

    private ComponentSvgWriter(ComponentDiagram diagram)
    {
        _diagram = diagram;
        _elements = diagram.Elements.ToDictionary(element => element.Id, StringComparer.Ordinal);
        _boxes = diagram.Elements.Where(element => !element.IsContainer).ToDictionary(element => element.Id, ComponentShapes.Create, StringComparer.Ordinal);
        _horizontal = diagram.LeftToRight;
        _axis = _horizontal ? new Point(1, 0) : new Point(0, 1);
        _cross = _horizontal ? new Point(0, 1) : new Point(1, 0);

        // Up and left links are laid out as down and right links in the other direction.
        _links = diagram.Links.Select((link, index) =>
        {
            var reversed = link.Direction is LinkDirection.Up or LinkDirection.Left;
            return new RoutedLink(
                index,
                link,
                reversed ? link.To : link.From,
                reversed ? link.From : link.To,
                reversed,
                link.Direction is LinkDirection.Left or LinkDirection.Right);
        }).ToList();

        foreach (var link in _links)
            if (Containers(link.From).Contains(link.To) || Containers(link.To).Contains(link.From))
                throw new DiagramSyntaxException("A link cannot connect a container with its own content.", 1);
    }

    public static string Write(ComponentDiagram diagram) => new ComponentSvgWriter(diagram).Write();

    private string Write()
    {
        var top = _diagram.Title == null ? 0 : TitleHeight;
        ContainerLayout root = null!;
        for (var pass = 0; pass < LayoutPasses; pass++)
        {
            root = _root = LayoutContainer(null);
            Place(root, new Point(0, top));
        }

        var containers = new StringBuilder();
        foreach (var element in _diagram.Elements.Where(element => element.IsContainer).OrderBy(element => Containers(element.Id).Count))
        {
            var layout = _containers[element.Id];
            var center = _centers[element.Id];
            ComponentShapes.WriteContainer(containers, element, center.X - layout.Width / 2, center.Y - layout.Height / 2, layout.Width, layout.Height);
        }

        var edges = new StringBuilder();
        var labels = new StringBuilder();
        var minX = 0.0;
        var maxX = root.ContentWidth;
        var minY = top;
        var maxY = top + root.ContentHeight;
        var routes = RouteLinks();
        foreach (var link in _links)
        {
            if (!routes.TryGetValue(link.Index, out var route)) continue;
            var (points, labelCenter) = route;
            WriteLink(edges, link, points);
            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            if (link.Link.Label == null || labelCenter is not { } center) continue;
            var label = NodeShapes.CreateLabel(link.Link.Label);
            NodeShapes.WriteLabel(labels, label, center);
            minX = Math.Min(minX, center.X - label.Width / 2);
            maxX = Math.Max(maxX, center.X + label.Width / 2);
            minY = Math.Min(minY, center.Y - label.Height / 2);
            maxY = Math.Max(maxY, center.Y + label.Height / 2);
        }

        var nodes = new StringBuilder();
        foreach (var element in _diagram.Elements.Where(element => !element.IsContainer))
            ComponentShapes.WriteElement(nodes, _boxes[element.Id], _centers[element.Id], element.Href);

        var title = new StringBuilder();
        if (_diagram.Title != null)
        {
            var lines = TextMetrics.Wrap(_diagram.Title, double.MaxValue);
            var width = TextMetrics.Measure(lines).Width;
            var center = root.ContentWidth / 2;
            minX = Math.Min(minX, center - width / 2);
            maxX = Math.Max(maxX, center + width / 2);

            // Links that detour above the content push the title up, so that they stay clear of it.
            var titleTop = minY - top;
            WriteText(title, lines, new Point(center, titleTop + TitleHeight / 2 - 2), "diagram-component-title");
            minY = titleTop;
        }

        var svgWidth = maxX - minX + 2 * CanvasPadding;
        var svgHeight = maxY - minY + 2 * CanvasPadding;
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-component\"")
            .Append($" viewBox=\"{F(minX - CanvasPadding)} {F(minY - CanvasPadding)} {F(svgWidth)} {F(svgHeight)}\"")
            .Append($" width=\"{F(svgWidth)}\" height=\"{F(svgHeight)}\"")
            .Append($" font-size=\"{F(TextMetrics.FontSize)}\" role=\"img\" aria-label=\"{Escape(_diagram.Title ?? "Component diagram")}\">\n")
            .Append("<g class=\"diagram-containers\">\n").Append(containers)
            .Append("</g>\n<g class=\"diagram-edges\">\n").Append(edges)
            .Append("</g>\n<g class=\"diagram-edge-labels\">\n").Append(labels)
            .Append("</g>\n<g class=\"diagram-nodes\">\n").Append(nodes)
            .Append("</g>\n").Append(title)
            .Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Lays out the children of a container (or of the diagram for <c>null</c>), after their own content.</summary>
    private ContainerLayout LayoutContainer(string? containerId)
    {
        var children = _diagram.Elements.Where(element => element.Parent == containerId).ToList();
        foreach (var child in children.Where(child => child.IsContainer)) _containers[child.Id] = LayoutContainer(child.Id);

        var nodes = children.Select(child => child.IsContainer
            ? new LayoutNode(_containers[child.Id].Width, _containers[child.Id].Height)
            : new LayoutNode(_boxes[child.Id].Width, _boxes[child.Id].Height)).ToList();
        var index = children.Select((child, position) => (child.Id, position)).ToDictionary(item => item.Id, item => item.position, StringComparer.Ordinal);
        var edges = new List<LayoutEdge>();
        var roles = new List<(int Link, Role Role)>();

        foreach (var link in _links)
        {
            var from = ChildOf(containerId, link.From);
            var to = ChildOf(containerId, link.To);
            if (from == null && to == null) continue;
            if (from != null && to != null && from == to) continue;

            var sourcePort = from != null && from != link.From ? _containers[from].Ports[(link.Index, Role.Exit)] : (double?)null;
            var targetPort = to != null && to != link.To ? _containers[to].Ports[(link.Index, Role.Enter)] : (double?)null;
            if (from != null && to != null)
            {
                var flat = link.Flat && from == link.From && to == link.To;
                var label = link.Link.Label == null || link.Link.Hidden ? null : NodeShapes.CreateLabel(link.Link.Label);
                edges.Add(new LayoutEdge(index[from], index[to], flat ? 0 : link.Link.Length, label?.Width ?? 0, label?.Height ?? 0, sourcePort, targetPort));
                roles.Add((link.Index, Role.Through));
            }
            else if (from != null)
            {
                nodes.Add(new LayoutNode(0, 0, Pin: LayoutPin.Last, Anchor: PortAnchor(containerId, link.To)));
                edges.Add(new LayoutEdge(index[from], nodes.Count - 1, SourcePort: sourcePort));
                roles.Add((link.Index, Role.Exit));
            }
            else
            {
                nodes.Add(new LayoutNode(0, 0, Pin: LayoutPin.First, Anchor: PortAnchor(containerId, link.From)));
                edges.Add(new LayoutEdge(nodes.Count - 1, index[to!], TargetPort: targetPort));
                roles.Add((link.Index, Role.Enter));
            }
        }

        var result = new LayeredLayout { CallerRoutesLinks = true, RankSeparation = RankSeparation }.Compute(nodes, edges, _horizontal ? LayoutDirection.LeftToRight : LayoutDirection.TopToBottom);
        var layout = new ContainerLayout(result.Width, result.Height);
        for (var position = 0; position < children.Count; position++) layout.Centers[children[position].Id] = result.NodeCenters[position];
        for (var edge = 0; edge < edges.Count; edge++) layout.Routes[roles[edge]] = result.Edges[edge];

        if (containerId != null) FitContainer(_elements[containerId], layout, result, children, edges, roles);
        return layout;
    }

    /// <summary>
    /// Where a port would like to be: toward the far end of its link in the last pass, as far as the container's content
    /// reaches. This moves the content toward what it connects to, across the borders that the layouts stop at.
    /// </summary>
    private double? PortAnchor(string? containerId, string farEnd)
    {
        if (containerId == null || !_containers.TryGetValue(containerId, out var last) || !_centers.TryGetValue(farEnd, out var target)) return null;

        var children = _diagram.Elements.Where(element => element.Parent == containerId).ToList();
        if (children.Count == 0) return null;
        double Half(ComponentElement child) => (_horizontal ? SizeOf(child).Height : SizeOf(child).Width) / 2;
        var from = children.Min(child => last.Centers[child.Id].Dot(_cross) - Half(child));
        var to = children.Max(child => last.Centers[child.Id].Dot(_cross) + Half(child));
        return Math.Clamp((target - last.AbsoluteOrigin).Dot(_cross), from, to);
    }

    /// <summary>
    /// Sizes a container box around its children and the label. The port ranks above and below the children are cut
    /// off: routes to a port end at the box border instead, where the parent's route continues.
    /// </summary>
    private void FitContainer(
        ComponentElement element, ContainerLayout layout, LayoutResult result, List<ComponentElement> children,
        List<LayoutEdge> edges, List<(int Link, Role Role)> roles)
    {
        double Along(Point point) => point.Dot(_axis);
        double Across(Point point) => point.Dot(_cross);

        var alongMin = 0.0;
        var alongMax = 0.0;
        if (children.Count > 0)
        {
            alongMin = double.PositiveInfinity;
            alongMax = double.NegativeInfinity;
            for (var position = 0; position < children.Count; position++)
            {
                var size = SizeOf(children[position]);
                var half = (_horizontal ? size.Width : size.Height) / 2;
                alongMin = Math.Min(alongMin, Along(result.NodeCenters[position]) - half);
                alongMax = Math.Max(alongMax, Along(result.NodeCenters[position]) + half);
            }
        }

        // The label sits above the content, which is before the content in top-to-bottom layouts and beside it otherwise.
        var header = ComponentShapes.HeaderHeight(element);
        var padding = ComponentShapes.ContainerPadding;
        var before = _horizontal ? padding : header;
        var crossBefore = _horizontal ? header : padding;
        var crossExtent = _horizontal ? result.Height : result.Width;
        var alongLength = alongMax - alongMin + before + padding;
        var crossLength = crossExtent + crossBefore + padding;
        layout.Width = Math.Max(_horizontal ? alongLength : crossLength, ComponentShapes.MinContainerWidth(element));
        layout.Height = _horizontal ? crossLength : alongLength;
        layout.Origin = _horizontal
            ? new Point(before - alongMin, crossBefore)
            : new Point((layout.Width - crossExtent) / 2, before - alongMin);

        var borderBefore = alongMin - before;
        var borderAfter = alongMax + padding;
        for (var edge = 0; edge < edges.Count; edge++)
        {
            var role = roles[edge].Role;
            if (role == Role.Through) continue;

            var route = layout.Routes[roles[edge]];
            // A port can lie beside the content, which it must not widen; its link then leaves at the content's edge.
            var across = Math.Clamp(Across(result.NodeCenters[role == Role.Exit ? edges[edge].Target : edges[edge].Source]), 0, crossExtent);
            var border = _cross * across + _axis * (role == Role.Exit ? borderAfter : borderBefore);
            var points = role == Role.Exit
                ? route.Points.TakeWhile(point => Along(point) < borderAfter).Append(border)
                : route.Points.SkipWhile(point => Along(point) <= borderBefore).Prepend(border);
            layout.Routes[roles[edge]] = route with { Points = points.ToList() };

            // Port positions along the border, relative to the container's center, for the parent's layout.
            layout.Ports[roles[edge]] = _horizontal
                ? layout.Origin.Y + across - layout.Height / 2
                : layout.Origin.X + across - layout.Width / 2;
        }
    }

    private (double Width, double Height) SizeOf(ComponentElement element) =>
        element.IsContainer ? (_containers[element.Id].Width, _containers[element.Id].Height) : (_boxes[element.Id].Width, _boxes[element.Id].Height);

    private void Place(ContainerLayout layout, Point origin)
    {
        layout.AbsoluteOrigin = origin;
        foreach (var (id, center) in layout.Centers)
        {
            var absolute = origin + center;
            _centers[id] = absolute;
            if (!_containers.TryGetValue(id, out var child)) continue;
            Place(child, new Point(absolute.X - child.Width / 2, absolute.Y - child.Height / 2) + child.Origin);
        }
    }

    /// <summary>
    /// Routes all drawn links. The layouts of the containers do not know each other, so the joined pieces bend at every
    /// border. Once everything is placed, the links drop the bends they do not need and their ends spread by direction.
    /// </summary>
    private Dictionary<int, (List<Point> Points, Point? Label)> RouteLinks()
    {
        var routes = new Dictionary<int, (List<Point> Points, Point? Label)>();
        foreach (var link in _links.Where(link => !link.Link.Hidden))
        {
            var route = Route(link);
            if (route.Points.Count >= 2) routes[link.Index] = route;
        }

        FaceOtherEnds(routes);
        SeparateDoubles(routes);
        Straighten(routes, AllObstacles(routes));
        SpreadPorts(routes);
        PlaceFreeLabels(routes);
        var obstacles = AllObstacles(routes);
        Straighten(routes, obstacles);
        TrimEnds(routes, obstacles);
        AttachInterfaces(routes, obstacles);
        return routes;
    }

    private Dictionary<int, List<Bounds>> AllObstacles(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        var edgeLabels = EdgeLabels(routes);
        return routes.Keys.ToDictionary(index => index, index => Obstacles(_links[index], edgeLabels, LabelRoom(_links[index])));
    }

    /// <summary>The label of a flat link stays on its route wherever that leads, so the route keeps room for the label.</summary>
    private double LabelRoom(RoutedLink link)
    {
        if (!link.Flat || link.Link.Label is not { } text) return 0;
        var label = NodeShapes.CreateLabel(text);
        return (_horizontal ? label.Width : label.Height) / 2;
    }

    private Dictionary<int, Bounds> EdgeLabels(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        var edgeLabels = new Dictionary<int, Bounds>();
        foreach (var (index, route) in routes)
        {
            if (_links[index].Link.Label is not { } text || route.Label is not { } center) continue;
            var label = NodeShapes.CreateLabel(text);
            edgeLabels[index] = Around(center, label.Width, label.Height);
        }

        return edgeLabels;
    }

    private static Bounds Around(Point center, double width, double height) =>
        new(center.X - width / 2, center.Y - height / 2, center.X + width / 2, center.Y + height / 2);

    /// <summary>
    /// A link in a cycle between containers can arrive on the far side of an element, because the layouts only see the
    /// containers. Such an end moves to the side that faces the other end, so the link does not run around the element.
    /// </summary>
    private void FaceOtherEnds(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        void Face(string id, List<Point> points, int end, int otherEnd)
        {
            if (_boxes.TryGetValue(id, out var box) && box.Kind == ElementKind.Interface) return;
            var size = SizeOf(_elements[id]);
            var half = (_horizontal ? size.Width : size.Height) / 2;
            var along = (points[end] - _centers[id]).Dot(_axis);
            var toward = (points[otherEnd] - _centers[id]).Dot(_axis);
            if (Math.Abs(along) >= half - 1 && Math.Abs(toward) > half && Math.Sign(along) != Math.Sign(toward))
                points[end] -= _axis * (2 * along);
        }

        foreach (var link in _links)
        {
            if (!routes.TryGetValue(link.Index, out var route)) continue;
            Face(link.Link.From, route.Points, 0, route.Points.Count - 1);
            Face(link.Link.To, route.Points, route.Points.Count - 1, 0);
        }
    }

    private void Straighten(Dictionary<int, (List<Point> Points, Point? Label)> routes, Dictionary<int, List<Bounds>> obstacles)
    {
        foreach (var (index, route) in routes.ToList()) routes[index] = Straighten(route.Points, route.Label, obstacles[index]);
    }

    /// <summary>Takes the shortest way around the obstacles. A label's point stays on the route.</summary>
    private static (List<Point> Points, Point? Label) Straighten(List<Point> points, Point? label, List<Bounds> obstacles)
    {
        var stop = label is { } center ? LabelStop(points, center) : -1;
        if (stop < 0) return (ShortestPath(points, obstacles), label);

        var result = ShortestPath(points.GetRange(0, stop + 1), obstacles);
        result.AddRange(ShortestPath(points.GetRange(stop, points.Count - stop), obstacles).Skip(1));
        return (result, label);
    }

    private static int LabelStop(List<Point> points, Point label) =>
        points.FindIndex(1, Math.Max(0, points.Count - 2), point => (point - label).Length < 0.5);

    /// <summary>
    /// A label between the points of its route, as on a flat link, takes the free spot nearest to where it is along the route
    /// and becomes a point of it, so that the last routing keeps both the link and the other links in line with it.
    /// </summary>
    private void PlaceFreeLabels(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        foreach (var (index, route) in routes.ToList())
        {
            if (route.Label is not { } center || LabelStop(route.Points, center) >= 0) continue;

            var link = _links[index];
            var label = NodeShapes.CreateLabel(link.Link.Label!);
            var taken = Obstacles(link, EdgeLabels(routes));
            taken.Add(BoundsOf(_elements[link.From]));
            taken.Add(BoundsOf(_elements[link.To]));

            var chord = route.Points[^1] - route.Points[0];
            var wanted = Math.Clamp((center - route.Points[0]).Dot(chord) / Math.Max(chord.Dot(chord), 1e-9), 0.05, 0.95);
            var spots = Enumerable.Range(1, 19).Select(step => step * 0.05).OrderBy(share => Math.Abs(share - wanted)).Prepend(wanted)
                .Select(share => PointAlong(route.Points, share))
                .ToList();
            var spot = spots.FirstOrDefault(candidate => !taken.Any(Around(candidate.Point, label.Width, label.Height).Overlaps), spots[0]);
            if ((spot.Point - route.Points[spot.Segment]).Length >= 0.5 && (spot.Point - route.Points[spot.Segment + 1]).Length >= 0.5)
                route.Points.Insert(spot.Segment + 1, spot.Point);
            routes[index] = (route.Points, spot.Point);
        }
    }

    private static (Point Point, int Segment) PointAlong(List<Point> path, double share)
    {
        var remaining = path.Zip(path.Skip(1)).Sum(segment => (segment.Second - segment.First).Length) * share;
        for (var segment = 0; segment < path.Count - 1; segment++)
        {
            var delta = path[segment + 1] - path[segment];
            if (remaining <= delta.Length) return (path[segment] + delta.Normalized * remaining, segment);
            remaining -= delta.Length;
        }

        return (path[^1], path.Count - 2);
    }

    /// <summary>Links whose ends coincide, such as repeated flat links, would be drawn on top of each other, so they move apart and their labels line up along them.</summary>
    private void SeparateDoubles(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        (long, long) Rounded(Point point) => ((long)Math.Round(point.X), (long)Math.Round(point.Y));

        var doubles = routes.GroupBy(route =>
        {
            var (first, last) = (Rounded(route.Value.Points[0]), Rounded(route.Value.Points[^1]));
            return first.CompareTo(last) <= 0 ? (first, last) : (last, first);
        });
        foreach (var group in doubles.Where(group => group.Count() > 1).ToList())
        {
            var points = group.First().Value.Points;
            var (start, chord) = (points[0], points[^1] - points[0]);
            var normal = chord.Normalized.Perpendicular;
            double Breadth(string id) => Math.Abs(normal.X) * SizeOf(_elements[id]).Width + Math.Abs(normal.Y) * SizeOf(_elements[id]).Height;

            var link = _links[group.First().Key];
            var spacing = EndSpacing(Math.Min(Breadth(link.From), Breadth(link.To)), group.Count());
            double Extent(RoutedLink labeled)
            {
                var label = NodeShapes.CreateLabel(labeled.Link.Label!);
                return Math.Abs(normal.Y) * label.Width + Math.Abs(normal.X) * label.Height;
            }

            bool IsFreeLabel(int index) => routes[index].Label is { } center && LabelStop(routes[index].Points, center) < 0;
            var labeled = group.Where(route => IsFreeLabel(route.Key)).ToList();
            var gap = Math.Max(0, (chord.Length - labeled.Sum(route => Extent(_links[route.Key]))) / (labeled.Count + 1));
            var along = gap;
            foreach (var (route, position) in group.Select((route, position) => (route, position)))
            {
                var offset = normal * ((position - (group.Count() - 1) / 2.0) * spacing);
                var free = IsFreeLabel(route.Key);
                route.Value.Points[0] += offset;
                route.Value.Points[^1] += offset;
                if (!free) continue;

                var extent = Extent(_links[route.Key]);
                routes[route.Key] = (route.Value.Points, start + chord.Normalized * (along + extent / 2) + offset);
                along += extent + gap;
            }
        }
    }

    private static double EndSpacing(double breadth, int count) =>
        count > 1 ? Math.Min(PortSpacing, breadth * 2 * PortReach / (count - 1) * 0.9) : 0;

    /// <summary>
    /// A free straight line needs no search. Otherwise the search looks for a way around the obstacles near the route first,
    /// then around more of them, which is rarely needed. Without a free way the route stays as it is. An obstacle around an
    /// end, such as a large label, cannot be avoided and does not count.
    /// </summary>
    private static List<Point> ShortestPath(List<Point> route, List<Bounds> obstacles)
    {
        var (start, end) = (route[0], route[^1]);
        var avoidable = obstacles.Where(obstacle => !obstacle.Blocks(start, start) && !obstacle.Blocks(end, end)).ToList();
        if (!avoidable.Any(obstacle => obstacle.Blocks(start, end))) return [start, end];

        var area = new Bounds(route.Min(point => point.X), route.Min(point => point.Y), route.Max(point => point.X), route.Max(point => point.Y));
        var nearest = avoidable.OrderBy(obstacle => DistanceToChord(obstacle, start, end)).ToList();
        return ShortestPath(start, end, nearest.Where(area.Overlaps).Take(MaxDetourObstacles), avoidable)
            ?? ShortestPath(start, end, nearest.Take(2 * MaxDetourObstacles), avoidable)
            ?? route;
    }

    private static double DistanceToChord(Bounds obstacle, Point start, Point end)
    {
        var center = new Point((obstacle.Left + obstacle.Right) / 2, (obstacle.Top + obstacle.Bottom) / 2);
        var chord = end - start;
        var share = Math.Clamp((center - start).Dot(chord) / Math.Max(chord.Dot(chord), 1e-9), 0, 1);
        return (center - (start + chord * share)).Length;
    }

    /// <summary>A* over the corners of some obstacles, so a link only bends where something is in its way.</summary>
    private static List<Point>? ShortestPath(Point start, Point end, IEnumerable<Bounds> around, List<Bounds> obstacles)
    {
        var nodes = new List<Point> { start, end };
        nodes.AddRange(around.SelectMany(obstacle => obstacle.Corners).Where(corner => !obstacles.Any(obstacle => obstacle.Blocks(corner, corner))));

        // Every leg stays between the nodes, so only the obstacles in their area can block one.
        var reach = new Bounds(nodes.Min(node => node.X), nodes.Min(node => node.Y), nodes.Max(node => node.X), nodes.Max(node => node.Y));
        var blocking = obstacles.Where(reach.Overlaps).ToList();
        var remaining = nodes.Select(node => (end - node).Length).ToArray();
        var distance = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var done = new bool[nodes.Count];
        distance[0] = 0;
        while (true)
        {
            var current = -1;
            for (var index = 0; index < nodes.Count; index++)
                if (!done[index] && !double.IsInfinity(distance[index]) && (current < 0 || distance[index] + remaining[index] < distance[current] + remaining[current]))
                    current = index;
            if (current < 0) return null;
            if (current == 1) break;

            done[current] = true;
            for (var index = 0; index < nodes.Count; index++)
            {
                if (done[index]) continue;
                var length = distance[current] + (nodes[index] - nodes[current]).Length;
                if (length >= distance[index] || blocking.Any(obstacle => obstacle.Blocks(nodes[current], nodes[index]))) continue;
                distance[index] = length;
                previous[index] = current;
            }
        }

        var path = new List<Point>();
        for (var index = 1; index >= 0; index = previous[index]) path.Add(nodes[index]);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The last route decides where a link meets an interface: on the circle, or below the label if it would cross the
    /// label to what lies below. What lies beside a wide label is reached over the label's corner.
    /// </summary>
    private void AttachInterfaces(Dictionary<int, (List<Point> Points, Point? Label)> routes, Dictionary<int, List<Bounds>> obstacles)
    {
        // The new end can have something in its way that the end before had not.
        void KeepFree(List<Point> points, bool start, List<Bounds> around)
        {
            var path = ShortestPath(start ? [points[0], points[1]] : [points[^2], points[^1]], around);
            points.InsertRange(start ? 1 : points.Count - 1, path.Skip(1).Take(path.Count - 2));
        }

        var below = new Dictionary<string, List<(List<Point> Points, bool Start, Point Toward, List<Bounds> Around)>>(StringComparer.Ordinal);
        void Attach(string id, List<Point> points, bool start, List<Bounds> around)
        {
            if (!_boxes.TryGetValue(id, out var box) || box.Kind != ElementKind.Interface) return;
            Point Neighbor() => start ? points[1] : points[^2];

            var circle = ComponentShapes.InterfaceCenter(box, _centers[id]);
            var bounds = BoundsOf(_elements[id]);
            var label = bounds with { Top = circle.Y + ComponentShapes.InterfaceRadiusValue };
            if (label.Blocks(circle, Neighbor()))
            {
                if (Neighbor().Y > bounds.Bottom)
                {
                    if (!below.TryGetValue(id, out var ends)) below[id] = ends = new List<(List<Point>, bool, Point, List<Bounds>)>();
                    ends.Add((points, start, Neighbor(), around));
                    return;
                }

                points.Insert(start ? 1 : points.Count - 1, new Point(Neighbor().X > circle.X ? label.Right + 0.5 : label.Left - 0.5, label.Top - 0.5));
            }

            points[start ? 0 : points.Count - 1] = circle + (Neighbor() - circle).Normalized * ComponentShapes.InterfaceRadiusValue;
            KeepFree(points, start, around);
        }

        foreach (var (index, route) in routes)
        {
            Attach(_links[index].Link.From, route.Points, true, obstacles[index]);
            Attach(_links[index].Link.To, route.Points, false, obstacles[index]);
        }

        foreach (var (id, ends) in below)
        {
            var spacing = EndSpacing(_boxes[id].Width, ends.Count);
            var ordered = ends.OrderBy(end => end.Toward.X).ToList();
            for (var position = 0; position < ordered.Count; position++)
            {
                var (points, start, _, around) = ordered[position];
                points[start ? 0 : points.Count - 1] = new Point(_centers[id].X + (position - (ordered.Count - 1) / 2.0) * spacing, _centers[id].Y + _boxes[id].Height / 2);
                KeepFree(points, start, around);
            }
        }
    }

    /// <summary>An end needs room for its marker and a clear approach, so it drops the bends next to it that cramp it, unless that leads through an obstacle.</summary>
    private void TrimEnds(Dictionary<int, (List<Point> Points, Point? Label)> routes, Dictionary<int, List<Bounds>> obstacles)
    {
        foreach (var (index, (points, label)) in routes)
        {
            bool CanDrop(Point bend, Point from, Point to) =>
                (label is not { } center || (bend - center).Length >= 0.5) && !obstacles[index].Any(obstacle => obstacle.Blocks(from, to));

            var link = _links[index].Link;
            while (points.Count > 2 && CrampedEnd(points[^1], points[^2], Marker(link.ToHead)) && CanDrop(points[^2], points[^3], points[^1])) points.RemoveAt(points.Count - 2);
            while (points.Count > 2 && CrampedEnd(points[0], points[1], Marker(link.FromHead)) && CanDrop(points[1], points[0], points[2])) points.RemoveAt(1);
        }
    }

    /// <summary>What a link keeps away from: other elements, foreign containers, the labels of its own containers, and the labels of other links.</summary>
    private List<Bounds> Obstacles(RoutedLink link, Dictionary<int, Bounds> edgeLabels, double labelRoom = 0)
    {
        var (from, to) = (BoundsOf(_elements[link.From]), BoundsOf(_elements[link.To]));
        var own = Containers(link.From).Concat(Containers(link.To)).ToHashSet(StringComparer.Ordinal);
        var obstacles = new List<Bounds>();
        foreach (var element in _diagram.Elements)
        {
            var box = BoundsOf(element);
            if (element.Id == link.From || element.Id == link.To)
            {
                // A link does not run back through its own ends, but it may touch them. An interface's link starts inside its box.
                if (!_boxes.TryGetValue(element.Id, out var end) || end.Kind != ElementKind.Interface) obstacles.Add(box.Inflated(-1));
            }
            else if (own.Contains(element.Id)) obstacles.Add(LabelBounds(element));
            else
            {
                // A link's label needs room across the link only, and a link must still be able to leave its ends.
                var kept = box.Inflated(ObstacleMargin);
                var roomy = _horizontal ? kept with { Left = kept.Left - labelRoom, Right = kept.Right + labelRoom } : kept with { Top = kept.Top - labelRoom, Bottom = kept.Bottom + labelRoom };
                obstacles.Add(roomy.Overlaps(from) || roomy.Overlaps(to) ? kept : roomy);
            }
        }

        obstacles.AddRange(edgeLabels.Where(label => label.Key != link.Index).Select(label => label.Value.Inflated(EdgeLabelMargin)));
        return obstacles;
    }

    private Bounds BoundsOf(ComponentElement element)
    {
        var center = _centers[element.Id];
        var (width, height) = SizeOf(element);
        return new Bounds(center.X - width / 2, center.Y - height / 2, center.X + width / 2, center.Y + height / 2);
    }

    /// <summary>A container's label with the room that links keep from it; the content right below it stays reachable.</summary>
    private Bounds LabelBounds(ComponentElement container)
    {
        if (_labelBounds.TryGetValue(container.Id, out var known)) return known;

        var box = BoundsOf(container);
        var (left, width, height) = ComponentShapes.LabelArea(container, box.Left, box.Right - box.Left);
        return _labelBounds[container.Id] = new Bounds(left - ObstacleMargin, box.Top - ObstacleMargin, left + width + ObstacleMargin, box.Top + height + ObstacleMargin / 2);
    }

    /// <summary>Orders the link ends on each side of an element by where they lead, so they leave without crossing.</summary>
    private void SpreadPorts(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        double Across(Point point) => point.Dot(_cross);

        var sides = new Dictionary<(string Id, int Side), List<(int Link, bool Start, Point Toward)>>();
        void Add(int linkIndex, string id, Point end, Point neighbor, bool start)
        {
            if (_boxes.TryGetValue(id, out var box) && box.Kind == ElementKind.Interface) return;
            var size = SizeOf(_elements[id]);
            var along = (end - _centers[id]).Dot(_axis);
            if (Math.Abs(along) < (_horizontal ? size.Width : size.Height) / 2 - 1) return;

            var key = (id, Math.Sign(along));
            if (!sides.TryGetValue(key, out var side)) sides[key] = side = new List<(int, bool, Point)>();
            side.Add((linkIndex, start, neighbor));
        }

        foreach (var link in _links)
        {
            if (!routes.TryGetValue(link.Index, out var route)) continue;
            Add(link.Index, link.Link.From, route.Points[0], route.Points[1], true);
            Add(link.Index, link.Link.To, route.Points[^1], route.Points[^2], false);
        }

        foreach (var ((id, sideOf), side) in sides)
        {
            var size = SizeOf(_elements[id]);
            var breadth = _horizontal ? size.Height : size.Width;
            var center = Across(_centers[id]);
            var spacing = EndSpacing(breadth, side.Count);
            var spread = (side.Count - 1) / 2.0 * spacing;
            var room = Math.Max(0, breadth * PortReach - spread);
            var lean = Math.Clamp((side.Average(end => Across(end.Toward)) - center) * PortLean, -room, room);
            var ordered = side.OrderBy(end => Across(end.Toward)).ToList();
            Point End(int position) => _cross * (center + lean + position * spacing - spread) + _axis * (_centers[id].Dot(_axis) + sideOf * (_horizontal ? size.Width : size.Height) / 2);

            // Links that come down through a container's label move beside it and closer together, if the side is wide enough.
            if (!_horizontal && sideOf < 0)
                foreach (var container in Containers(id).Select(container => LabelBounds(_elements[container])))
                {
                    if (!ordered.Select((end, position) => (end.Toward, Port: End(position).X)).Any(end => end.Toward.Y <= container.Bottom + 1 && end.Port >= container.Left && end.Port <= container.Right)) continue;

                    var free = center + breadth / 2 - PortInset - (container.Right + 1);
                    if (free < 0) continue;
                    spacing = ordered.Count > 1 ? Math.Min(spacing, free / (ordered.Count - 1)) : 0;
                    spread = (ordered.Count - 1) / 2.0 * spacing;
                    lean = container.Right + 1 - center + spread;
                }

            for (var position = 0; position < ordered.Count; position++)
            {
                var points = routes[ordered[position].Link].Points;
                var index = ordered[position].Start ? 0 : points.Count - 1;
                points[index] += _cross * (End(position).Dot(_cross) - Across(points[index]));
            }
        }
    }

    /// <summary>Joins the pieces of a link's route: out of its source's containers, across, and into its target's.</summary>
    private (List<Point> Points, Point? Label) Route(RoutedLink link)
    {
        var fromContainers = Containers(link.From);
        var toContainers = Containers(link.To);
        var common = fromContainers.FirstOrDefault(toContainers.Contains);

        var points = new List<Point>();
        Point? label = null;
        void Append(string? containerId, Role role)
        {
            var layout = containerId == null ? _root! : _containers[containerId];
            if (!layout.Routes.TryGetValue((link.Index, role), out var route)) return;
            foreach (var point in route.Points)
            {
                var absolute = layout.AbsoluteOrigin + point;
                if (points.Count == 0 || (points[^1] - absolute).Length > 0.5) points.Add(absolute);
            }

            if (role == Role.Through && route.LabelCenter is { } center) label = layout.AbsoluteOrigin + center;
        }

        foreach (var container in fromContainers.TakeWhile(container => container != common)) Append(container, Role.Exit);
        Append(common, Role.Through);
        foreach (var container in toContainers.TakeWhile(container => container != common).Reverse()) Append(container, Role.Enter);

        if (link.Reversed) points.Reverse();
        return (points, label);
    }

    private void WriteLink(StringBuilder svg, RoutedLink link, List<Point> points)
    {
        var fromHead = Marker(link.Link.FromHead);
        var toHead = Marker(link.Link.ToHead);

        // A label's point on a straight stretch is no bend.
        for (var index = points.Count - 2; index >= 1; index--)
        {
            var (before, after) = ((points[index] - points[index - 1]).Normalized, (points[index + 1] - points[index]).Normalized);
            if (before.Dot(after) > 0 && Math.Abs(before.Dot(after.Perpendicular)) < 0.001) points.RemoveAt(index);
        }

        var startDirection = (points[1] - points[0]).Normalized;
        var endDirection = (points[^1] - points[^2]).Normalized;
        var start = points[0];
        var end = points[^1];

        // A line stops short of its markers, but not beyond the next bend, where it would double back.
        double Inset(MarkerKind head, Point segment) => Math.Min(SvgMarkers.Inset(head), points.Count == 2 ? segment.Length / 2 : segment.Length);
        var (firstSegment, lastSegment) = (points[1] - start, end - points[^2]);
        points[0] = start + startDirection * Inset(fromHead, firstSegment);
        points[^1] = end - endDirection * Inset(toHead, lastSegment);

        // Straight segments with rounded corners: unlike a spline through the points, they cannot overshoot.
        var path = new StringBuilder($"M{F(points[0].X)} {F(points[0].Y)}");
        for (var index = 1; index < points.Count - 1; index++)
        {
            var toPrevious = points[index - 1] - points[index];
            var toNext = points[index + 1] - points[index];
            var radius = Math.Min(CornerRadius, Math.Min(toPrevious.Length, toNext.Length) / 2);
            var entry = points[index] + toPrevious.Normalized * radius;
            var exit = points[index] + toNext.Normalized * radius;
            path.Append($" L{F(entry.X)} {F(entry.Y)} Q{F(points[index].X)} {F(points[index].Y)} {F(exit.X)} {F(exit.Y)}");
        }

        path.Append($" L{F(points[^1].X)} {F(points[^1].Y)}");

        var lineClass = link.Link.Line switch
        {
            LinkLine.Dotted => "diagram-edge diagram-edge-dotted",
            LinkLine.Bold => "diagram-edge diagram-edge-thick",
            _ => "diagram-edge"
        };
        svg.Append($"<path class=\"{lineClass}\" d=\"{path}\"/>");
        SvgMarkers.Write(svg, toHead, end, endDirection);
        SvgMarkers.Write(svg, fromHead, start, startDirection * -1);
        svg.Append('\n');
    }

    private bool CrampedEnd(Point end, Point neighbor, MarkerKind head)
    {
        var segment = end - neighbor;
        var along = Math.Abs(segment.Dot(_axis));
        return segment.Length < SvgMarkers.Inset(head) + 4 || (along < MinApproach && Math.Abs(segment.Dot(_cross)) > 4 * along);
    }

    private static MarkerKind Marker(LinkHead head) => head switch
    {
        LinkHead.Arrow => MarkerKind.Arrow,
        LinkHead.Triangle => MarkerKind.Triangle,
        _ => MarkerKind.None
    };

    /// <summary>The containers around an element, innermost first.</summary>
    private List<string> Containers(string id)
    {
        var result = new List<string>();
        for (var parent = _elements[id].Parent; parent != null; parent = _elements[parent].Parent) result.Add(parent);
        return result;
    }

    /// <summary>The direct child of a container (or of the diagram) that holds an element, or <c>null</c> if it is outside.</summary>
    private string? ChildOf(string? containerId, string id)
    {
        for (var current = id; current != null; current = _elements[current].Parent)
            if (_elements[current].Parent == containerId)
                return current;
        return null;
    }

    private enum Role
    {
        Through,
        Exit,
        Enter
    }

    private sealed record RoutedLink(int Index, ComponentLink Link, string From, string To, bool Reversed, bool Flat);

    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public Bounds Inflated(double by) => new(Left - by, Top - by, Right + by, Bottom + by);

        public bool Overlaps(Bounds other) => Left <= other.Right && other.Left <= Right && Top <= other.Bottom && other.Top <= Bottom;

        /// <summary>The corners, just outside so that a route can pass them.</summary>
        public Point[] Corners =>
        [
            new(Left - 0.5, Top - 0.5), new(Right + 0.5, Top - 0.5),
            new(Left - 0.5, Bottom + 0.5), new(Right + 0.5, Bottom + 0.5)
        ];

        /// <summary>Whether the segment between two points runs through this area (Liang–Barsky clipping).</summary>
        public bool Blocks(Point from, Point to)
        {
            var enter = 0.0;
            var leave = 1.0;
            var delta = to - from;
            return Clip(-delta.X, from.X - Left, ref enter, ref leave)
                && Clip(delta.X, Right - from.X, ref enter, ref leave)
                && Clip(-delta.Y, from.Y - Top, ref enter, ref leave)
                && Clip(delta.Y, Bottom - from.Y, ref enter, ref leave);
        }

        private static bool Clip(double direction, double distance, ref double enter, ref double leave)
        {
            if (direction == 0) return distance >= 0;

            var share = distance / direction;
            if (direction < 0) enter = Math.Max(enter, share);
            else leave = Math.Min(leave, share);
            return enter <= leave;
        }
    }

    private sealed class ContainerLayout
    {
        public ContainerLayout(double contentWidth, double contentHeight)
        {
            ContentWidth = contentWidth;
            ContentHeight = contentHeight;
            Width = contentWidth;
            Height = contentHeight;
        }

        public double ContentWidth { get; }
        public double ContentHeight { get; }

        /// <summary>The size of the container box; equal to the content for the diagram itself.</summary>
        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Where the content starts inside the box.</summary>
        public Point Origin { get; set; }

        public Point AbsoluteOrigin { get; set; }
        public Dictionary<string, Point> Centers { get; } = new(StringComparer.Ordinal);
        public Dictionary<(int Link, Role Role), EdgeRoute> Routes { get; } = new();
        public Dictionary<(int Link, Role Role), double> Ports { get; } = new();
    }
}
