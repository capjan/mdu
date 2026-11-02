using StaticCode.Diagrams.Layout;

namespace StaticCode.Diagrams.Test;

public class LayeredLayoutTest
{
    private static readonly LayoutNode Box = new(60, 30);

    [Fact]
    public void PlacesTargetsBelowSourcesFromTopToBottom()
    {
        var result = Compute(4, new[] { (0, 1), (1, 2), (0, 3), (3, 2) }, LayoutDirection.TopToBottom);

        Assert.True(result.NodeCenters[1].Y > result.NodeCenters[0].Y);
        Assert.True(result.NodeCenters[2].Y > result.NodeCenters[1].Y);
        Assert.Equal(result.NodeCenters[1].Y, result.NodeCenters[3].Y);
    }

    [Fact]
    public void PlacesTargetsRightOfSourcesFromLeftToRight()
    {
        var result = Compute(3, new[] { (0, 1), (1, 2) }, LayoutDirection.LeftToRight);

        Assert.True(result.NodeCenters[1].X > result.NodeCenters[0].X);
        Assert.True(result.NodeCenters[2].X > result.NodeCenters[1].X);
        Assert.Equal(result.NodeCenters[0].Y, result.NodeCenters[2].Y, 1);
    }

    [Fact]
    public void ReversesDirectionsForBottomToTopAndRightToLeft()
    {
        var bottomToTop = Compute(2, new[] { (0, 1) }, LayoutDirection.BottomToTop);
        var rightToLeft = Compute(2, new[] { (0, 1) }, LayoutDirection.RightToLeft);

        Assert.True(bottomToTop.NodeCenters[1].Y < bottomToTop.NodeCenters[0].Y);
        Assert.True(rightToLeft.NodeCenters[1].X < rightToLeft.NodeCenters[0].X);
    }

    [Fact]
    public void KeepsNodesOfARankApart()
    {
        var result = Compute(5, new[] { (0, 1), (0, 2), (0, 3), (0, 4) }, LayoutDirection.TopToBottom);

        var children = result.NodeCenters.Skip(1).OrderBy(point => point.X).ToList();
        for (var index = 1; index < children.Count; index++)
            Assert.True(children[index].X - children[index - 1].X >= Box.Width);
    }

    [Fact]
    public void CentersAParentAboveItsChildren()
    {
        var result = Compute(3, new[] { (0, 1), (0, 2) }, LayoutDirection.TopToBottom);

        Assert.Equal((result.NodeCenters[1].X + result.NodeCenters[2].X) / 2, result.NodeCenters[0].X, 1);
    }

    [Fact]
    public void RoutesCyclesFromSourceToTarget()
    {
        var result = Compute(3, new[] { (0, 1), (1, 2), (2, 0) }, LayoutDirection.TopToBottom);

        var back = result.Edges[2].Points;
        Assert.True(back.Count >= 3);
        Assert.Equal(result.NodeCenters[2].Y - Box.Height / 2, back[0].Y, 3);
        Assert.Equal(result.NodeCenters[0].Y + Box.Height / 2, back[^1].Y, 3);
    }

    [Fact]
    public void AvoidsCrossingsWhenPossible()
    {
        // Declaring the children in the "wrong" order would cross the edges without reordering.
        var result = Compute(4, new[] { (0, 3), (1, 2), (0, 2) }, LayoutDirection.TopToBottom);

        var sourcesInOrder = result.NodeCenters[0].X < result.NodeCenters[1].X;
        var targetsInOrder = result.NodeCenters[3].X < result.NodeCenters[2].X;
        Assert.Equal(sourcesInOrder, targetsInOrder);
    }

    [Fact]
    public void PlacesEdgeLabelsBetweenTheirNodes()
    {
        var nodes = new[] { Box, Box };
        var edges = new[] { new LayoutEdge(0, 1, LabelWidth: 40, LabelHeight: 20) };

        var result = new LayeredLayout().Compute(nodes, edges, LayoutDirection.TopToBottom);

        Assert.NotNull(result.Edges[0].LabelCenter);
        var label = result.Edges[0].LabelCenter!.Value;
        Assert.True(label.Y - 10 > result.NodeCenters[0].Y + Box.Height / 2);
        Assert.True(label.Y + 10 < result.NodeCenters[1].Y - Box.Height / 2);
    }

    [Fact]
    public void ReturnsEmptyRoutesForSelfLoops()
    {
        var result = Compute(1, new[] { (0, 0) }, LayoutDirection.TopToBottom);

        Assert.Empty(result.Edges[0].Points);
    }

    [Fact]
    public void LongerLinksSpanMoreRanks()
    {
        var nodes = new[] { Box, Box, Box };
        var edges = new[] { new LayoutEdge(0, 1), new LayoutEdge(0, 2, MinLength: 2) };

        var result = new LayeredLayout().Compute(nodes, edges, LayoutDirection.TopToBottom);

        Assert.True(result.NodeCenters[2].Y > result.NodeCenters[1].Y);
    }

    [Fact]
    public void LinesUpPortsInsteadOfCenters()
    {
        var edges = new[] { new LayoutEdge(0, 1, SourcePort: 20, TargetPort: -10) };

        var result = new LayeredLayout().Compute(new[] { Box, Box }, edges, LayoutDirection.TopToBottom);

        Assert.Equal(result.NodeCenters[0].X + 20, result.NodeCenters[1].X - 10, 1);
    }

    [Fact]
    public void AnchorsPullNodesAlongTheLayer()
    {
        var nodes = new[] { Box, new LayoutNode(60, 30, Anchor: 300), new LayoutNode(60, 30, Anchor: 0) };
        var edges = new[] { new LayoutEdge(0, 1), new LayoutEdge(0, 2) };

        var result = new LayeredLayout().Compute(nodes, edges, LayoutDirection.TopToBottom);

        Assert.True(result.NodeCenters[1].X - result.NodeCenters[2].X > 100);
    }

    [Fact]
    public void DirectShortLinksSkipTheBendRank()
    {
        var edges = new[] { new LayoutEdge(0, 1) };

        var direct = new LayeredLayout { DirectShortLinks = true }.Compute(new[] { Box, Box }, edges, LayoutDirection.TopToBottom);
        var bent = new LayeredLayout().Compute(new[] { Box, Box }, edges, LayoutDirection.TopToBottom);

        Assert.Equal(2, direct.Edges[0].Points.Count);
        Assert.Equal(3, bent.Edges[0].Points.Count);
    }

    [Fact]
    public void KeepsFlatEdgesInOneRankInOrder()
    {
        var nodes = new[] { Box, Box, Box };
        var edges = new[] { new LayoutEdge(1, 0, MinLength: 0), new LayoutEdge(0, 2) };

        var result = new LayeredLayout().Compute(nodes, edges, LayoutDirection.TopToBottom);

        Assert.Equal(result.NodeCenters[0].Y, result.NodeCenters[1].Y, 3);
        Assert.True(result.NodeCenters[1].X < result.NodeCenters[0].X);
        var route = result.Edges[0].Points;
        Assert.Equal(2, route.Count);
        Assert.Equal(result.NodeCenters[1].X + Box.Width / 2, route[0].X, 3);
        Assert.Equal(result.NodeCenters[0].X - Box.Width / 2, route[1].X, 3);
    }

    [Fact]
    public void PinsNodesToTheFirstAndLastRank()
    {
        var nodes = new[] { Box, Box, Box, new LayoutNode(0, 0, Pin: LayoutPin.First), new LayoutNode(0, 0, Pin: LayoutPin.Last) };
        var edges = new[] { new LayoutEdge(0, 1), new LayoutEdge(1, 2), new LayoutEdge(3, 2), new LayoutEdge(0, 4) };

        var result = new LayeredLayout().Compute(nodes, edges, LayoutDirection.TopToBottom);

        Assert.True(result.NodeCenters[3].Y < result.NodeCenters[0].Y);
        Assert.True(result.NodeCenters[4].Y > result.NodeCenters[2].Y);
    }

    [Fact]
    public void UsesGivenPortOffsets()
    {
        var result = new LayeredLayout().Compute(new[] { Box, Box }, new[] { new LayoutEdge(0, 1, SourcePort: -20, TargetPort: 15) }, LayoutDirection.TopToBottom);

        Assert.Equal(result.NodeCenters[0].X - 20, result.Edges[0].Points[0].X, 3);
        Assert.Equal(result.NodeCenters[1].X + 15, result.Edges[0].Points[^1].X, 3);
    }

    private static LayoutResult Compute(int nodeCount, (int Source, int Target)[] edges, LayoutDirection direction) =>
        new LayeredLayout().Compute(
            Enumerable.Repeat(Box, nodeCount).ToArray(),
            edges.Select(edge => new LayoutEdge(edge.Source, edge.Target)).ToArray(),
            direction);
}
