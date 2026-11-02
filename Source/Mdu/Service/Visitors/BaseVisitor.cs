using Mdu.Service.PageTree;
using Mdu.Model;


namespace Mdu.Service.Visitors;

public abstract class BaseVisitor : IVisitor<MarkdownFile>
{
    private readonly string _rootPath;
    private readonly string _outPath;
    private readonly string? _outputFileExtension;

    protected BaseVisitor(string rootPath, string outPath, string? outputFileExtension = default, TraversalAlgorithm algorithm = TraversalAlgorithm.Default)
    {
        _rootPath = rootPath;
        _outPath = outPath;
        _outputFileExtension = outputFileExtension;
        Algorithm = algorithm;
    }

    public TraversalAlgorithm Algorithm { get; }
    
    public bool Process(MarkdownFile entity, int graphDepth)
    {
        var pathInfo = FilePathInfo.CreateRenderFileInfo(
            _rootPath,
            _outPath,
            entity.FileInfo,
            _outputFileExtension,
            entity.SiteRelativePath);
        return ProcessFile(entity, graphDepth, pathInfo);
    }

    protected abstract bool ProcessFile(MarkdownFile entity, int graphDepth, FilePathInfo pathInfo);
}
