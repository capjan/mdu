using Mdu.Service.PageTree;

namespace Mdu.Service.Visitors;

public class CountNodesVisitor<T> : IVisitor<T>
{
    public TraversalAlgorithm Algorithm => TraversalAlgorithm.Default;
    public int NodeCount { get; private set; }

    public bool Process(T entity, int graphDepth)
    {
        NodeCount++;
        return true;
    }

}