using Mdu.Service.PageTree;
using Mdu.Model;

namespace Mdu.Service.Visitors;

public class PrintTreeVisitor : IVisitor<MarkdownFile>
{
    public TraversalAlgorithm Algorithm => TraversalAlgorithm.DepthFirst;
    public bool Process(MarkdownFile entity, int graphDepth)
    {
        var indentString = new string(' ', graphDepth*2);
        Console.WriteLine($"{indentString}{entity.Title}");
        return true;
    }
}
