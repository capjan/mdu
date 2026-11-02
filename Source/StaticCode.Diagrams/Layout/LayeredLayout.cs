namespace StaticCode.Diagrams.Layout;

internal enum LayoutDirection
{
    TopToBottom,
    BottomToTop,
    LeftToRight,
    RightToLeft
}

/// <summary>Keeps a node in the first or last rank, for example a port where an edge enters or leaves a container.</summary>
internal enum LayoutPin
{
    None,
    First,
    Last
}

/// <param name="Margin">Extra free space on both sides along the layer, for example for self-loops.</param>
/// <param name="Anchor">Where along the layer the node wants to be, for example toward the far end of a port's link.</param>
internal sealed record LayoutNode(double Width, double Height, double Margin = 0, LayoutPin Pin = LayoutPin.None, double? Anchor = null);

/// <param name="MinLength">
/// Minimum number of ranks between source and target. 0 makes a flat edge: both ends stay in the same rank when the
/// other edges allow it, with the source before the target within the rank.
/// </param>
/// <param name="SourcePort">Offset of the edge end along the source's side, instead of an evenly spread port.</param>
/// <param name="TargetPort">Offset of the edge end along the target's side, instead of an evenly spread port.</param>
internal sealed record LayoutEdge(
    int Source,
    int Target,
    int MinLength = 1,
    double LabelWidth = 0,
    double LabelHeight = 0,
    double? SourcePort = null,
    double? TargetPort = null);

/// <param name="Points">
/// Route from source to target. The first and last points lie on the bounding boxes of the end nodes;
/// the points in between are where the edge passes between ranks. Empty for self-loops.
/// </param>
internal sealed record EdgeRoute(IReadOnlyList<Point> Points, Point? LabelCenter);

internal sealed record LayoutResult(IReadOnlyList<Point> NodeCenters, IReadOnlyList<EdgeRoute> Edges, double Width, double Height);

/// <summary>
/// Layered graph drawing after Sugiyama et al.: break cycles, assign ranks, insert dummy vertices for long edges
/// and labels, reduce crossings with barycenter sweeps and transpositions, then place vertices by minimizing the
/// weighted squared length of all segments subject to the separation constraints.
/// </summary>
internal sealed class LayeredLayout
{
    private const int OrderingIterations = 24;
    private const int TransposePasses = 8;
    private const int PositioningIterations = 400;
    private const double AnchorWeight = 2;

    // Very large layouts of callers that route their links get fewer iterations in place of a growing run time.
    private const int OrderingWork = 72_000;
    private const int MinOrderingIterations = 6;
    private const int PositioningWork = 600_000;
    private const int MinPositioningIterations = 60;

    public double NodeSeparation { get; init; } = 36;
    public double EdgeSeparation { get; init; } = 14;
    public double RankSeparation { get; init; } = 52;
    public double PortSpacing { get; init; } = 14;

    /// <summary>
    /// For callers that route the links themselves once everything is placed: unlabeled edges between neighboring ranks
    /// stay direct instead of getting a bend rank of their own, and connected nodes line up in preference to the
    /// dummies of long edges, which then only keep room free.
    /// </summary>
    public bool CallerRoutesLinks { get; init; }

    // Flat edges that ended up in one rank: the left vertex must stay before the right one.
    private readonly List<(Vertex Left, Vertex Right)> _flatPairs = new();
    private readonly HashSet<(Vertex Left, Vertex Right)> _flatPairSet = new();
    private readonly Dictionary<(Vertex Left, Vertex Right), double> _flatGaps = new();

    public LayoutResult Compute(IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, LayoutDirection direction)
    {
        var horizontal = direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;
        var vertices = nodes.Select(node => new Vertex
        {
            Breadth = horizontal ? node.Height : node.Width,
            Depth = horizontal ? node.Width : node.Height,
            Margin = node.Margin,
            IsPort = node.Pin != LayoutPin.None,
            Anchor = node.Anchor
        }).ToList();
        if (vertices.Count == 0)
            return new LayoutResult(Array.Empty<Point>(), edges.Select(_ => new EdgeRoute(Array.Empty<Point>(), null)).ToArray(), 0, 0);

        _flatPairs.Clear();
        _flatPairSet.Clear();
        _flatGaps.Clear();

        // Every edge that is not flat spans at least two ranks so that labels and bends get a rank of their own.
        var reversed = FindBackEdges(nodes.Count, edges);
        var ranked = edges
            .Select((edge, index) => (Edge: edge, Index: index))
            .Where(item => item.Edge.Source != item.Edge.Target)
            .Select(item => new RankedEdge(
                item.Index,
                reversed[item.Index] ? item.Edge.Target : item.Edge.Source,
                reversed[item.Index] ? item.Edge.Source : item.Edge.Target,
                item.Edge.MinLength <= 0 ? 0 : CallerRoutesLinks && item.Edge.LabelWidth == 0 ? item.Edge.MinLength : 2 * item.Edge.MinLength))
            .ToList();

        AssignRanks(vertices, ranked, nodes.Select(node => node.Pin).ToArray());

        var chains = new List<Vertex>?[edges.Count];
        var labels = new Vertex?[edges.Count];
        var allVertices = new List<Vertex>(vertices);
        foreach (var edge in ranked)
        {
            var input = edges[edge.Index];
            var from = vertices[edge.Upper];
            var to = vertices[edge.Lower];
            if (from.Rank == to.Rank)
            {
                var left = vertices[input.Source];
                var right = vertices[input.Target];
                _flatPairs.Add((left, right));
                _flatPairSet.Add((left, right));
                if (input.LabelWidth > 0)
                {
                    var gap = (horizontal ? input.LabelHeight : input.LabelWidth) + 16;
                    _flatGaps[(left, right)] = CallerRoutesLinks ? _flatGaps.GetValueOrDefault((left, right)) + gap : gap;
                }

                continue;
            }

            var upperPort = (reversed[edge.Index] ? input.TargetPort : input.SourcePort) ?? 0;
            var lowerPort = (reversed[edge.Index] ? input.SourcePort : input.TargetPort) ?? 0;
            var labelRank = from.Rank + (to.Rank - from.Rank) / 2;
            var chain = new List<Vertex> { from };
            var previous = from;
            for (var rank = from.Rank + 1; rank < to.Rank; rank++)
            {
                var dummy = new Vertex { IsDummy = true, Rank = rank };
                if (rank == labelRank && input.LabelWidth > 0)
                {
                    dummy.Breadth = horizontal ? input.LabelHeight : input.LabelWidth;
                    dummy.Depth = horizontal ? input.LabelWidth : input.LabelHeight;
                    labels[edge.Index] = dummy;
                }

                Connect(previous, dummy, previous == from ? upperPort : 0, 0);
                chain.Add(dummy);
                allVertices.Add(dummy);
                previous = dummy;
            }

            Connect(previous, to, previous == from ? upperPort : 0, lowerPort);
            chain.Add(to);
            chains[edge.Index] = chain;
        }

        var layers = BuildLayers(vertices, allVertices.Max(vertex => vertex.Rank) + 1);
        MinimizeCrossings(layers);
        OrderAnchoredLayers(layers);
        AssignCoordinates(layers);
        foreach (var vertex in vertices) AssignPorts(vertex);

        var layoutWidth = Content(allVertices).Max(vertex => vertex.X + vertex.HalfBreadth);
        var layoutHeight = allVertices.Max(vertex => vertex.Y + vertex.Depth / 2);
        Point Map(double x, double y) => direction switch
        {
            LayoutDirection.BottomToTop => new Point(x, layoutHeight - y),
            LayoutDirection.LeftToRight => new Point(y, x),
            LayoutDirection.RightToLeft => new Point(layoutHeight - y, x),
            _ => new Point(x, y)
        };

        var routes = new EdgeRoute[edges.Count];
        for (var index = 0; index < edges.Count; index++)
        {
            var input = edges[index];
            var chain = chains[index];
            if (chain == null)
            {
                routes[index] = input.Source != input.Target && vertices[input.Source].Rank == vertices[input.Target].Rank
                    ? FlatRoute(vertices[input.Source], vertices[input.Target], input.LabelWidth > 0, Map)
                    : new EdgeRoute(Array.Empty<Point>(), null);
                continue;
            }

            var first = chain[0];
            var last = chain[^1];
            var firstPort = (reversed[index] ? input.TargetPort : input.SourcePort) ?? chain[1].UpperPort;
            var lastPort = (reversed[index] ? input.SourcePort : input.TargetPort) ?? chain[^2].LowerPort;
            var points = new List<Point> { Map(first.X + firstPort, first.Y + first.Depth / 2) };
            points.AddRange(chain.Skip(1).Take(chain.Count - 2).Select(dummy => Map(dummy.X, dummy.Y)));
            points.Add(Map(last.X + lastPort, last.Y - last.Depth / 2));
            if (reversed[index]) points.Reverse();

            var label = labels[index];
            routes[index] = new EdgeRoute(points, label == null ? null : Map(label.X, label.Y));
        }

        return new LayoutResult(
            vertices.Select(vertex => Map(vertex.X, vertex.Y)).ToArray(),
            routes,
            horizontal ? layoutHeight : layoutWidth,
            horizontal ? layoutWidth : layoutHeight);
    }

    /// <summary>A flat edge runs straight from the side of one vertex to the facing side of the other.</summary>
    private static EdgeRoute FlatRoute(Vertex source, Vertex target, bool labeled, Func<double, double, Point> map)
    {
        var direction = target.X >= source.X ? 1 : -1;
        var start = source.X + direction * source.Breadth / 2;
        var end = target.X - direction * target.Breadth / 2;
        return new EdgeRoute(
            new[] { map(start, source.Y), map(end, target.Y) },
            labeled ? map((start + end) / 2, source.Y) : null);
    }

    private static bool[] FindBackEdges(int nodeCount, IReadOnlyList<LayoutEdge> edges)
    {
        var reversed = new bool[edges.Count];
        var state = new byte[nodeCount];
        var outgoing = Enumerable.Range(0, nodeCount).Select(_ => new List<int>()).ToArray();
        for (var index = 0; index < edges.Count; index++)
            if (edges[index].Source != edges[index].Target)
                outgoing[edges[index].Source].Add(index);

        void Visit(int node)
        {
            state[node] = 1;
            foreach (var index in outgoing[node])
            {
                var target = edges[index].Target;
                if (state[target] == 1) reversed[index] = true;
                else if (state[target] == 0) Visit(target);
            }

            state[node] = 2;
        }

        for (var node = 0; node < nodeCount; node++)
            if (state[node] == 0)
                Visit(node);
        return reversed;
    }

    private static void AssignRanks(List<Vertex> vertices, List<RankedEdge> edges, LayoutPin[] pins)
    {
        var count = vertices.Count;
        var outgoing = Enumerable.Range(0, count).Select(_ => new List<RankedEdge>()).ToArray();
        var incoming = new int[count];
        foreach (var edge in edges)
        {
            outgoing[edge.Upper].Add(edge);
            incoming[edge.Lower]++;
        }

        var remaining = (int[])incoming.Clone();
        var queue = new Queue<int>(Enumerable.Range(0, count).Where(node => remaining[node] == 0));
        var topological = new List<int>(count);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            topological.Add(node);
            foreach (var edge in outgoing[node])
                if (--remaining[edge.Lower] == 0)
                    queue.Enqueue(edge.Lower);
        }

        var rank = new int[count];
        foreach (var node in topological)
        foreach (var edge in outgoing[node])
            rank[edge.Lower] = Math.Max(rank[edge.Lower], rank[node] + edge.MinLength);

        // Longest-path ranking pushes everything up; move nodes down when that shortens more edges than it stretches.
        for (var index = topological.Count - 1; index >= 0; index--)
        {
            var node = topological[index];
            if (outgoing[node].Count > 0 && outgoing[node].Count > incoming[node])
                rank[node] = outgoing[node].Min(edge => rank[edge.Lower] - edge.MinLength);
        }

        // Pinned nodes go one rank above or below everything else.
        var free = Enumerable.Range(0, count).Where(node => pins[node] == LayoutPin.None).ToList();
        if (free.Count < count)
        {
            var first = free.Count > 0 ? free.Min(node => rank[node]) : 0;
            var last = free.Count > 0 ? free.Max(node => rank[node]) : 0;
            for (var node = 0; node < count; node++)
                if (pins[node] != LayoutPin.None)
                    rank[node] = pins[node] == LayoutPin.First ? first - 1 : last + 1;
        }

        var lowest = rank.Min();
        for (var node = 0; node < count; node++) vertices[node].Rank = rank[node] - lowest;
    }

    /// <summary>Connects two vertices; the ports are where the edge ends, relative to the centers along the layer.</summary>
    private static void Connect(Vertex upper, Vertex lower, double upperPort, double lowerPort)
    {
        upper.Lower.Add(lower);
        upper.LowerOffsets.Add(lowerPort - upperPort);
        lower.Upper.Add(upper);
        lower.UpperOffsets.Add(upperPort - lowerPort);
    }

    private List<List<Vertex>> BuildLayers(List<Vertex> vertices, int layerCount)
    {
        var layers = Enumerable.Range(0, layerCount).Select(_ => new List<Vertex>()).ToList();
        var visited = new HashSet<Vertex>();

        void Visit(Vertex vertex)
        {
            if (!visited.Add(vertex)) return;
            layers[vertex.Rank].Add(vertex);
            foreach (var lower in vertex.Lower) Visit(lower);
        }

        foreach (var vertex in vertices.OrderBy(vertex => vertex.Rank)) Visit(vertex);
        foreach (var layer in layers) EnforceFlatOrder(layer);
        return layers;
    }

    private static void UpdateOrder(List<Vertex> layer)
    {
        for (var index = 0; index < layer.Count; index++) layer[index].Order = index;
    }

    private int Iterations(List<List<Vertex>> layers, int full, int work, int least) =>
        CallerRoutesLinks ? Math.Min(full, Math.Max(least, work / Math.Max(1, layers.Sum(layer => layer.Count)))) : full;

    private void MinimizeCrossings(List<List<Vertex>> layers)
    {
        var best = layers.Select(layer => layer.ToArray()).ToList();
        var bestCrossings = CountCrossings(layers);
        var iterations = Iterations(layers, OrderingIterations, OrderingWork, MinOrderingIterations);
        for (var iteration = 0; iteration < iterations && bestCrossings > 0; iteration++)
        {
            if (iteration % 2 == 0)
                for (var rank = 1; rank < layers.Count; rank++)
                    SortByBarycenter(layers[rank], vertex => vertex.Upper);
            else
                for (var rank = layers.Count - 2; rank >= 0; rank--)
                    SortByBarycenter(layers[rank], vertex => vertex.Lower);

            Transpose(layers);
            var crossings = CountCrossings(layers);
            if (crossings >= bestCrossings) continue;
            bestCrossings = crossings;
            best = layers.Select(layer => layer.ToArray()).ToList();
        }

        for (var rank = 0; rank < layers.Count; rank++)
        {
            layers[rank].Clear();
            layers[rank].AddRange(best[rank]);
            EnforceFlatOrder(layers[rank]);
        }
    }

    /// <summary>Sorts vertices by the mean position of their neighbors; vertices without neighbors keep their slot.</summary>
    private void SortByBarycenter(List<Vertex> layer, Func<Vertex, List<Vertex>> neighbors)
    {
        var movable = layer
            .Where(vertex => neighbors(vertex).Count > 0)
            .OrderBy(vertex => neighbors(vertex).Average(neighbor => neighbor.Order))
            .ThenBy(vertex => vertex.Order)
            .ToList();
        var next = 0;
        for (var index = 0; index < layer.Count; index++)
            if (neighbors(layer[index]).Count > 0)
                layer[index] = movable[next++];
        EnforceFlatOrder(layer);
    }

    /// <summary>
    /// Puts the right end of every flat edge after its left end. A right end without other edges, such as a note,
    /// moves directly next to its left end.
    /// </summary>
    private void EnforceFlatOrder(List<Vertex> layer)
    {
        UpdateOrder(layer);
        if (_flatPairs.Count == 0 || layer.Count == 0) return;

        var rank = layer[0].Rank;
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            foreach (var (left, right) in _flatPairs)
            {
                if (left.Rank != rank || right.Rank != rank) continue;
                var loose = right.Upper.Count == 0 && right.Lower.Count == 0;
                if (right.Order > left.Order && (!loose || right.Order == left.Order + 1)) continue;
                layer.Remove(right);
                layer.Insert(layer.IndexOf(left) + 1, right);
                UpdateOrder(layer);
                changed = true;
            }

            if (!changed) return;
        }
    }

    private void Transpose(List<List<Vertex>> layers)
    {
        for (var pass = 0; pass < TransposePasses; pass++)
        {
            var improved = false;
            foreach (var layer in layers)
            {
                for (var index = 0; index + 1 < layer.Count; index++)
                {
                    var left = layer[index];
                    var right = layer[index + 1];
                    if (_flatPairSet.Contains((left, right))) continue;
                    if (PairCrossings(right, left) >= PairCrossings(left, right)) continue;
                    layer[index] = right;
                    layer[index + 1] = left;
                    right.Order = index;
                    left.Order = index + 1;
                    improved = true;
                }
            }

            if (!improved) return;
        }
    }

    private static int PairCrossings(Vertex left, Vertex right)
    {
        var crossings = 0;
        foreach (var a in left.Upper)
        foreach (var b in right.Upper)
            if (a.Order > b.Order) crossings++;
        foreach (var a in left.Lower)
        foreach (var b in right.Lower)
            if (a.Order > b.Order) crossings++;
        return crossings;
    }

    /// <summary>Counts the pairs of segments between two layers whose order flips, with a Fenwick tree over the lower ends.</summary>
    private static int CountCrossings(List<List<Vertex>> layers)
    {
        var crossings = 0;
        foreach (var layer in layers)
        {
            var segments = layer.SelectMany(upper => upper.Lower.Select(lower => (upper.Order, lower: lower.Order))).ToList();
            if (segments.Count < 2) continue;

            segments.Sort();
            var tree = new int[segments.Max(segment => segment.lower) + 2];
            for (var seen = 0; seen < segments.Count; seen++)
            {
                var atOrBefore = 0;
                for (var slot = segments[seen].lower + 1; slot > 0; slot -= slot & -slot) atOrBefore += tree[slot];
                crossings += seen - atOrBefore;
                for (var slot = segments[seen].lower + 1; slot < tree.Length; slot += slot & -slot) tree[slot]++;
            }
        }

        return crossings;
    }

    /// <summary>Ports mark where edges leave, so they do not count toward the size of the layout.</summary>
    private static List<Vertex> Content(List<Vertex> vertices)
    {
        var content = vertices.Where(vertex => !vertex.IsPort).ToList();
        return content.Count > 0 ? content : vertices;
    }

    private static void OrderAnchoredLayers(List<List<Vertex>> layers)
    {
        foreach (var layer in layers.Where(layer => layer.Count > 1 && layer.All(vertex => vertex.Anchor != null)))
        {
            var ordered = layer.OrderBy(vertex => vertex.Anchor).ToList();
            layer.Clear();
            layer.AddRange(ordered);
            UpdateOrder(layer);
        }
    }

    private void AssignCoordinates(List<List<Vertex>> layers)
    {
        foreach (var layer in layers)
            for (var index = 0; index < layer.Count; index++)
                layer[index].X = index == 0 ? layer[index].HalfBreadth : layer[index - 1].X + Gap(layer[index - 1], layer[index]);

        var iterations = Iterations(layers, PositioningIterations, PositioningWork, MinPositioningIterations);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var moved = 0.0;
            if (iteration % 2 == 0)
                foreach (var layer in layers) moved = Math.Max(moved, PlaceLayer(layer));
            else
                for (var rank = layers.Count - 1; rank >= 0; rank--) moved = Math.Max(moved, PlaceLayer(layers[rank]));
            if (moved < 0.001) break;
        }

        var all = layers.SelectMany(layer => layer).ToList();
        var minX = Content(all).Min(vertex => vertex.X - vertex.HalfBreadth);
        foreach (var vertex in all) vertex.X -= minX;

        double? previousCenter = null;
        var previousDepth = 0.0;
        foreach (var layer in layers.Where(layer => layer.Count > 0))
        {
            var depth = layer.Max(vertex => vertex.Depth);
            var center = previousCenter == null
                ? depth / 2
                : previousCenter.Value + previousDepth / 2 + RankSeparation / 2 + depth / 2;
            foreach (var vertex in layer) vertex.Y = center;
            previousCenter = center;
            previousDepth = depth;
        }
    }

    /// <summary>
    /// Moves every vertex of a layer as close as possible to the weighted mean of its neighbors while keeping the
    /// order and minimum gaps. This is a weighted isotonic regression, solved exactly with pool-adjacent-violators.
    /// </summary>
    private double PlaceLayer(List<Vertex> layer)
    {
        var count = layer.Count;
        if (count == 0) return 0;

        var values = new double[count];
        var weights = new double[count];
        var offsets = new double[count];
        for (var index = 0; index < count; index++)
        {
            var vertex = layer[index];
            double sum = 0, weight = 0;
            AddPull(vertex, vertex.Upper, vertex.UpperOffsets, ref sum, ref weight);
            AddPull(vertex, vertex.Lower, vertex.LowerOffsets, ref sum, ref weight);
            if (vertex.Anchor is { } anchor)
            {
                sum += AnchorWeight * anchor;
                weight += AnchorWeight;
            }

            offsets[index] = index == 0 ? 0 : offsets[index - 1] + Gap(layer[index - 1], vertex);

            // A vertex without edges moves up to its left neighbor instead of staying where it started.
            var target = weight > 0 ? sum / weight : index > 0 ? layer[index - 1].X + Gap(layer[index - 1], vertex) : vertex.X;
            values[index] = target - offsets[index];
            weights[index] = weight > 0 ? weight : 1e-3;
        }

        var fitted = FitNonDecreasing(values, weights);
        var moved = 0.0;
        for (var index = 0; index < count; index++)
        {
            var x = fitted[index] + offsets[index];
            moved = Math.Max(moved, Math.Abs(x - layer[index].X));
            layer[index].X = x;
        }

        return moved;
    }

    private void AddPull(Vertex vertex, List<Vertex> neighbors, List<double> offsets, ref double sum, ref double weight)
    {
        for (var position = 0; position < neighbors.Count; position++)
        {
            var neighbor = neighbors[position];
            var segmentWeight = vertex.IsDummy && neighbor.IsDummy ? 8
                : CallerRoutesLinks ? (vertex.IsDummy || neighbor.IsDummy ? 1 : 4)
                : vertex.IsDummy || neighbor.IsDummy ? 2 : 1;
            sum += segmentWeight * (neighbor.X + offsets[position]);
            weight += segmentWeight;
        }
    }

    private static double[] FitNonDecreasing(double[] values, double[] weights)
    {
        var blocks = new List<(double Value, double Weight, int Count)>();
        for (var index = 0; index < values.Length; index++)
        {
            var block = (Value: values[index], Weight: weights[index], Count: 1);
            while (blocks.Count > 0 && blocks[^1].Value > block.Value)
            {
                var last = blocks[^1];
                blocks.RemoveAt(blocks.Count - 1);
                var weight = last.Weight + block.Weight;
                block = ((last.Value * last.Weight + block.Value * block.Weight) / weight, weight, last.Count + block.Count);
            }

            blocks.Add(block);
        }

        var result = new double[values.Length];
        var position = 0;
        foreach (var block in blocks)
            for (var index = 0; index < block.Count; index++)
                result[position++] = block.Value;
        return result;
    }

    private double Gap(Vertex left, Vertex right)
    {
        var separation = left.IsThin && right.IsThin ? EdgeSeparation
            : left.IsThin || right.IsThin ? (NodeSeparation + EdgeSeparation) / 2
            : NodeSeparation;
        var flatGap = 0.0;
        if (_flatGaps.Count > 0)
        {
            flatGap = _flatGaps.GetValueOrDefault((left, right));
            if (CallerRoutesLinks) flatGap += _flatGaps.GetValueOrDefault((right, left));
        }

        return left.HalfBreadth + right.HalfBreadth + separation + flatGap;
    }

    /// <summary>Spreads the edge ends on each side of a node so that arrowheads do not overlap.</summary>
    private void AssignPorts(Vertex vertex)
    {
        var lower = vertex.Lower.OrderBy(neighbor => neighbor.X).ToList();
        var lowerOffsets = PortOffsets(vertex, lower.Count);
        for (var index = 0; index < lower.Count; index++) lower[index].UpperPort = lowerOffsets[index];

        var upper = vertex.Upper.OrderBy(neighbor => neighbor.X).ToList();
        var upperOffsets = PortOffsets(vertex, upper.Count);
        for (var index = 0; index < upper.Count; index++) upper[index].LowerPort = upperOffsets[index];
    }

    private double[] PortOffsets(Vertex vertex, int count)
    {
        var spacing = count > 1 ? Math.Min(PortSpacing, vertex.Breadth * 0.6 / (count - 1)) : 0;
        return Enumerable.Range(0, count).Select(index => (index - (count - 1) / 2.0) * spacing).ToArray();
    }

    private sealed record RankedEdge(int Index, int Upper, int Lower, int MinLength);

    private sealed class Vertex
    {
        /// <summary>Extent along the layer.</summary>
        public double Breadth;

        /// <summary>Extent across layers.</summary>
        public double Depth;

        public double Margin;
        public bool IsDummy;
        public bool IsPort;
        public double? Anchor;
        public int Rank;
        public int Order;
        public double X;
        public double Y;

        /// <summary>For dummies: offset of the edge end on the real vertex above.</summary>
        public double UpperPort;

        /// <summary>For dummies: offset of the edge end on the real vertex below.</summary>
        public double LowerPort;

        public readonly List<Vertex> Upper = new();
        public readonly List<Vertex> Lower = new();

        /// <summary>Where this vertex would line up with each neighbor, relative to that neighbor's center.</summary>
        public readonly List<double> UpperOffsets = new();

        public readonly List<double> LowerOffsets = new();

        public double HalfBreadth => Breadth / 2 + Margin;

        /// <summary>Takes no more room than an edge passing by.</summary>
        public bool IsThin => IsDummy || IsPort;
    }
}
