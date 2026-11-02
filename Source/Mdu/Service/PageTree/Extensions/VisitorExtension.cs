namespace Mdu.Service.PageTree.Extensions;

public static class VisitorExtension
{

    /// <summary>
    /// Starts visiting the given node and all of its child nodes
    /// </summary>
    /// <param name="node">included origin node of the visiting tour</param>
    /// <param name="visitor">the visitor that takes the tour</param>
    /// <typeparam name="T">Type of the node</typeparam>
    public static void Visit<T>(this T node, IVisitor<T> visitor) where T: INode<T>
    {
        InternalVisit(node, visitor);
    }

    /// <summary>
    /// Starts visiting the given nodes and all of its child nodes
    /// </summary>
    /// <param name="nodes">included origin nodes of the visiting tour</param>
    /// <param name="visitor">the visitor that takes the tour</param>
    /// <typeparam name="T">Type of the node</typeparam>
    public static void Visit<T>(this IEnumerable<T> nodes, IVisitor<T> visitor) where T : INode<T>
    {
        var algorithm = visitor.Algorithm;
        if (algorithm is TraversalAlgorithm.Default or TraversalAlgorithm.BreadthFirst)
        {
            foreach (var node in nodes)
                if (!visitor.Process(node, node.CountDepth()))
                    // Return if the visitor has finished it's tour
                    return;
            
            foreach (var node in nodes)
                if (!ExecuteVisitorBreadthFirst(node.Children, visitor, node.CountDepth() + 1))
                    return;
        }
        else if (algorithm is TraversalAlgorithm.DepthFirst)
        {
            foreach (var node in nodes)
                if (!InternalVisit(node, visitor))
                    return;
        }
    }

    private static bool InternalVisit<T>(this T node, IVisitor<T> visitor) where T : INode<T>
    {
        var graphDepth = node.CountDepth();
        if (!visitor.Process(node, graphDepth)) return false;
        switch (visitor.Algorithm)
        {
            case TraversalAlgorithm.BreadthFirst:
            case TraversalAlgorithm.Default:
                return ExecuteVisitorBreadthFirst(node.Children, visitor, graphDepth + 1);
            case TraversalAlgorithm.DepthFirst:
                return ExecuteVisitorDepthFirst(node.Children, visitor, graphDepth + 1);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool ExecuteVisitorBreadthFirst<T>(T[] list, IVisitor<T> visitor, int graphDepth) where T: INode<T>
    {

        foreach (var markdownFile in list)
        {
            if (!visitor.Process(markdownFile, graphDepth))
                return false;
        }

        foreach (var markdownFile in list)
        {
            if (!ExecuteVisitorBreadthFirst(markdownFile.Children, visitor, graphDepth + 1))
                return false;
        }

        return true;
    }

    private static bool ExecuteVisitorDepthFirst<T>(IEnumerable<T> list, IVisitor<T> visitor, int graphDepth) where T: INode<T>
    {
        foreach (var markdownFile in list)
        {
            if (!visitor.Process(markdownFile, graphDepth))
                return false;

            if (!ExecuteVisitorDepthFirst(markdownFile.Children, visitor, graphDepth + 1))
                return false;
        }

        return true;
    }
}