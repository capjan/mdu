namespace Mdu.Service.PageTree;

/// <summary>
/// Interface for the nodes of the graph
/// </summary>
/// <typeparam name="T"></typeparam>
public interface INode<out T> {
    
    /// <summary>
    /// The optional parent of a node
    /// </summary>
    T? Parent { get; }
    
    /// <summary>
    /// The children of the node
    /// </summary>
    T[] Children { get; }
}
