namespace Mdu.Service.PageTree;

/// <summary>
/// Visitor of a Node Graph
/// </summary>
/// <typeparam name="T">Type of the nodes in the graph</typeparam>
public interface IVisitor<in T>
{
    /// <summary>
    /// used traversal algorithm of the visitor 
    /// </summary>
    TraversalAlgorithm Algorithm { get; }
    
    /// <summary>
    /// Method that is called when the Visitor visits a node
    /// </summary>
    /// <param name="entity">The visited entity</param>
    /// <param name="graphDepth">depth of the node to the root node</param>
    /// <returns>true if the visitor should continue the tour or false if the visitor should exit the tour through the graph</returns>
    bool Process(T entity, int graphDepth);
}
