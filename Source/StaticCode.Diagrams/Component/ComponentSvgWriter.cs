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

    // Free space that a link keeps around elements and container labels.
    private const double ObstacleMargin = 6;
    private const double PortSpacing = 30;
    private const double CornerRadius = 16;

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
        var routes = RouteLinks();
        foreach (var link in _links)
        {
            if (!routes.TryGetValue(link.Index, out var route)) continue;
            var (points, labelCenter) = route;
            WriteLink(edges, link, points);
            if (link.Link.Label == null || labelCenter is not { } center) continue;
            var label = NodeShapes.CreateLabel(link.Link.Label);
            NodeShapes.WriteLabel(labels, label, center);
            minX = Math.Min(minX, center.X - label.Width / 2);
            maxX = Math.Max(maxX, center.X + label.Width / 2);
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
            WriteText(title, lines, new Point(center, TitleHeight / 2 - 2), "diagram-component-title");
        }

        var svgWidth = maxX - minX + 2 * CanvasPadding;
        var svgHeight = top + root.ContentHeight + 2 * CanvasPadding;
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"diagram-component\"")
            .Append($" viewBox=\"{F(minX - CanvasPadding)} {F(-CanvasPadding)} {F(svgWidth)} {F(svgHeight)}\"")
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

        var result = new LayeredLayout { DirectShortLinks = true, RankSeparation = RankSeparation }.Compute(nodes, edges, _horizontal ? LayoutDirection.LeftToRight : LayoutDirection.TopToBottom);
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
            var port = result.NodeCenters[role == Role.Exit ? edges[edge].Target : edges[edge].Source];
            var border = _cross * Across(port) + _axis * (role == Role.Exit ? borderAfter : borderBefore);
            var points = role == Role.Exit
                ? route.Points.TakeWhile(point => Along(point) < borderAfter).Append(border)
                : route.Points.SkipWhile(point => Along(point) <= borderBefore).Prepend(border);
            layout.Routes[roles[edge]] = route with { Points = points.ToList() };

            // Port positions along the border, relative to the container's center, for the parent's layout.
            layout.Ports[roles[edge]] = _horizontal
                ? layout.Origin.Y + port.Y - layout.Height / 2
                : layout.Origin.X + port.X - layout.Width / 2;
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
        Straighten(routes);
        SpreadPorts(routes);
        Straighten(routes);
        return routes;
    }

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

    private void Straighten(Dictionary<int, (List<Point> Points, Point? Label)> routes)
    {
        foreach (var link in _links)
        {
            if (!routes.TryGetValue(link.Index, out var route)) continue;
            routes[link.Index] = (Straighten(route.Points, route.Label, Obstacles(link)), route.Label);
        }
    }

    /// <summary>Takes the shortest way around the obstacles. A label's point stays on the route.</summary>
    private static List<Point> Straighten(List<Point> points, Point? label, List<Bounds> obstacles)
    {
        var stop = label is { } center ? points.FindIndex(1, Math.Max(0, points.Count - 2), point => (point - center).Length < 0.5) : -1;
        if (stop < 0) return ShortestPath(points, obstacles);

        var result = ShortestPath(points.GetRange(0, stop + 1), obstacles);
        result.AddRange(ShortestPath(points.GetRange(stop, points.Count - stop), obstacles).Skip(1));
        return result;
    }

    /// <summary>
    /// Dijkstra over the corners of the obstacles near a route, so a link only bends where something is in its way.
    /// Without a free way, for example when an end lies in an obstacle, the route stays as it is.
    /// </summary>
    private static List<Point> ShortestPath(List<Point> route, List<Bounds> obstacles)
    {
        var area = new Bounds(route.Min(point => point.X), route.Min(point => point.Y), route.Max(point => point.X), route.Max(point => point.Y));
        var nodes = new List<Point> { route[0], route[^1] };
        nodes.AddRange(obstacles.Where(area.Overlaps).SelectMany(obstacle => obstacle.Corners).Where(corner => !obstacles.Any(obstacle => obstacle.Blocks(corner, corner))));

        var distance = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var done = new bool[nodes.Count];
        distance[0] = 0;
        while (true)
        {
            var current = -1;
            for (var index = 0; index < nodes.Count; index++)
                if (!done[index] && !double.IsInfinity(distance[index]) && (current < 0 || distance[index] < distance[current]))
                    current = index;
            if (current < 0) return route;
            if (current == 1) break;

            done[current] = true;
            for (var index = 0; index < nodes.Count; index++)
            {
                if (done[index]) continue;
                var length = distance[current] + (nodes[index] - nodes[current]).Length;
                if (length >= distance[index] || obstacles.Any(obstacle => obstacle.Blocks(nodes[current], nodes[index]))) continue;
                distance[index] = length;
                previous[index] = current;
            }
        }

        var path = new List<Point>();
        for (var index = 1; index >= 0; index = previous[index]) path.Add(nodes[index]);
        path.Reverse();
        return path;
    }

    /// <summary>What a link keeps away from: other elements, foreign containers, and the labels of its own containers.</summary>
    private List<Bounds> Obstacles(RoutedLink link)
    {
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
            else obstacles.Add(own.Contains(element.Id) ? LabelBounds(element) : box.Inflated(ObstacleMargin));
        }

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
        var box = BoundsOf(container);
        var (left, width, height) = ComponentShapes.LabelArea(container, box.Left, box.Right - box.Left);
        return new Bounds(left - ObstacleMargin, box.Top - ObstacleMargin, left + width + ObstacleMargin, box.Top + height + ObstacleMargin / 2);
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
            var spacing = side.Count > 1 ? Math.Min(PortSpacing, breadth * 2 * PortReach / (side.Count - 1) * 0.9) : 0;
            var spread = (side.Count - 1) / 2.0 * spacing;
            var room = Math.Max(0, breadth * PortReach - spread);
            var lean = Math.Clamp((side.Average(end => Across(end.Toward)) - center) * PortLean, -room, room);
            var ordered = side.OrderBy(end => Across(end.Toward)).ToList();
            Point End(int position) => _cross * (center + lean + position * spacing - spread) + _axis * (_centers[id].Dot(_axis) + sideOf * (_horizontal ? size.Width : size.Height) / 2);

            // Links that come down through a container's label move beside it, if the side is wide enough.
            if (!_horizontal && sideOf < 0)
                foreach (var container in Containers(id).Select(container => LabelBounds(_elements[container])))
                    if (ordered.Select((end, position) => (end.Toward, Port: End(position).X)).Any(end => end.Toward.Y <= container.Bottom + 1 && end.Port >= container.Left && end.Port <= container.Right))
                        lean = Math.Clamp(container.Right + 1 - center + spread, -room, room);

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

        if (points.Count >= 2)
        {
            points[0] = Snap(link.From, points[0]);
            points[^1] = Snap(link.To, points[^1]);
        }

        if (link.Reversed) points.Reverse();
        return (points, label);
    }

    /// <summary>Links attach to an interface's circle rather than to its label.</summary>
    private Point Snap(string id, Point point)
    {
        if (!_boxes.TryGetValue(id, out var box) || box.Kind != ElementKind.Interface) return point;
        var circle = ComponentShapes.InterfaceCenter(box, _centers[id]);
        var offset = point - circle;
        if (!_horizontal && offset.Y > ComponentShapes.InterfaceRadiusValue + 1) return point;
        return circle + offset.Normalized * ComponentShapes.InterfaceRadiusValue;
    }

    private void WriteLink(StringBuilder svg, RoutedLink link, List<Point> points)
    {
        var fromHead = Marker(link.Link.FromHead);
        var toHead = Marker(link.Link.ToHead);
        var startDirection = (points[1] - points[0]).Normalized;
        var endDirection = (points[^1] - points[^2]).Normalized;
        var start = points[0];
        var end = points[^1];
        points[0] = start + startDirection * SvgMarkers.Inset(fromHead);
        points[^1] = end - endDirection * SvgMarkers.Inset(toHead);

        // Straight segments with rounded corners: unlike a spline through the points, they cannot overshoot.
        var path = new StringBuilder($"M{F(points[0].X)} {F(points[0].Y)}");
        for (var index = 1; index < points.Count - 1; index++)
        {
            var before = points[index - 1] - points[index];
            var after = points[index + 1] - points[index];
            var radius = Math.Min(CornerRadius, Math.Min(before.Length, after.Length) / 2);
            var entry = points[index] + before.Normalized * radius;
            var exit = points[index] + after.Normalized * radius;
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
            foreach (var (direction, distance) in new[]
                     {
                         (-delta.X, from.X - Left), (delta.X, Right - from.X),
                         (-delta.Y, from.Y - Top), (delta.Y, Bottom - from.Y)
                     })
            {
                if (direction == 0)
                {
                    if (distance < 0) return false;
                    continue;
                }

                var share = distance / direction;
                if (direction < 0) enter = Math.Max(enter, share);
                else leave = Math.Min(leave, share);
                if (enter > leave) return false;
            }

            return true;
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
