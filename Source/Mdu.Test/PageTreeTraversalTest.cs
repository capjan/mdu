using Mdu.Service.PageTree;
using Mdu.Service.PageTree.Extensions;

namespace Mdu.Test;

public class PageTreeTraversalTest
{
    [Theory]
    [InlineData(TraversalAlgorithm.DepthFirst, "root,a,a1,a1x,a2,b,b1,b1x")]
    [InlineData(TraversalAlgorithm.BreadthFirst, "root,a,b,a1,a2,a1x,b1,b1x")]
    [InlineData(TraversalAlgorithm.Default, "root,a,b,a1,a2,a1x,b1,b1x")]
    public void VisitsNodesInExistingOrder(TraversalAlgorithm algorithm, string expected)
    {
        var root = new Node("root");
        var a = root.Add("a");
        var b = root.Add("b");
        a.Add("a1").Add("a1x");
        a.Add("a2");
        b.Add("b1").Add("b1x");
        var visitor = new RecordingVisitor(algorithm);

        root.Visit(visitor);

        Assert.Equal(expected, string.Join(',', visitor.Names));
        Assert.Equal(visitor.Names.Select(name => name switch
        {
            "root" => 0,
            "a" or "b" => 1,
            "a1" or "a2" or "b1" => 2,
            _ => 3
        }), visitor.Depths);
        Assert.Equal(2, a.Children[0].CountDepth());
    }

    [Fact]
    public void StopsWhenVisitorReturnsFalse()
    {
        var root = new Node("root");
        root.Add("a").Add("child");
        root.Add("b");
        var visitor = new RecordingVisitor(TraversalAlgorithm.DepthFirst, "a");

        root.Visit(visitor);

        Assert.Equal(new[] { "root", "a" }, visitor.Names);
    }

    private sealed class Node(string name, Node? parent = null) : INode<Node>
    {
        public string Name { get; } = name;
        public Node? Parent { get; } = parent;
        public Node[] Children { get; private set; } = [];
        public Node Add(string name)
        {
            var child = new Node(name, this);
            Children = [.. Children, child];
            return child;
        }
    }

    private sealed class RecordingVisitor(TraversalAlgorithm algorithm, string? stopAt = null) : IVisitor<Node>
    {
        public TraversalAlgorithm Algorithm => algorithm;
        public List<string> Names { get; } = [];
        public List<int> Depths { get; } = [];
        public bool Process(Node entity, int graphDepth)
        {
            Names.Add(entity.Name);
            Depths.Add(graphDepth);
            return entity.Name != stopAt;
        }
    }
}
