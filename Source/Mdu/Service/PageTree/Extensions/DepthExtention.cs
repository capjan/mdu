namespace Mdu.Service.PageTree.Extensions;

public static class DepthExtension
{
    
    /// <summary>
    /// Returns the nesting depth of the node as integer
    /// </summary>
    /// <param name="node">node from which the nesting depth is to be determined</param>
    /// <typeparam name="T">Type of the node</typeparam>
    /// <returns>the nesting depth of the node to the root node.</returns>
    public static int CountDepth<T>(this T node) where T : INode<T>
    {
        var depth = 0;
        while (node.Parent is not null)
        {
            node = node.Parent;
            depth++;
        }

        return depth;
    }
}