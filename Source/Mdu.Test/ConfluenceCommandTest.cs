using Markdown.Confluence;
using Markdown.Document.Impl;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class ConfluenceCommandTest
{
    [Fact]
    public void PreservesTreeAndSourceFilesWithoutConfluenceMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-confluence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = Path.Combine(root, "README.md");
            var child = Path.Combine(root, "Child.md");
            File.WriteAllText(home, "---\ntitle: Home\n---\n# Home");
            File.WriteAllText(child, "# Child");
            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())).CreateGraph(
                new VisitorSettings { RootPath = root, IncludeHidden = true, SearchPattern = "*.md" });
            var sources = ConfluenceCommand.PrepareSources(graph, root);
            Assert.Equal(2, sources.Count);
            Assert.Null(sources.Single(source => source.Path == "README.md").ParentPath);
            Assert.Equal("README.md", sources.Single(source => source.Path == "Child.md").ParentPath);
            Assert.Equal("# Home", sources.Single(source => source.Path == "README.md").Markdown);
            Assert.Equal("---\ntitle: Home\n---\n# Home", File.ReadAllText(home));
            Assert.Equal("# Child", File.ReadAllText(child));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PreparesPdfOnlyWhenLinked()
    {
        ConfluenceSource[] sources = [new("README.md", null, "Home", "# Home", "/docs/README.md", "/docs")];
        Assert.Null(ConfluenceCommand.PreparePdf(sources, null));
        sources[0] = sources[0] with { Markdown = "# Home\n\n[Download](mdu:pdf)" };
        var pdf = ConfluenceCommand.PreparePdf(sources, null);
        Assert.NotNull(pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf!.Content[..5]));
        var document = new ConfluenceStorageRenderer(new ConfluenceTarget("https://wiki.example.test", "DOC"),
            sources, pdf: pdf).Render(sources[0]);
        Assert.Equal(pdf.Name, Assert.Single(document.Assets).Name);
    }

    [Fact]
    public void ConvertsRepositoryDocumentationBeforeAnyNetworkCalls()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Mdu.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "Docs");
        var configuration = SiteConfigurationReader.Read(root);
        var glossary = SiteConfigurationReader.ResolveGlossaryPath(root, configuration);
        var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())).CreateGraph(
            new VisitorSettings { RootPath = root, IncludeHidden = true, SearchPattern = "*.md" }, glossary);
        var sources = ConfluenceCommand.PrepareSources(graph, root);
        var glossaryText = glossary is null ? null : File.ReadAllText(glossary);
        var renderer = new ConfluenceStorageRenderer(new ConfluenceTarget("https://wiki.example.test", "DOC"),
            sources, glossaryText, ConfluenceCommand.PreparePdf(sources, glossaryText));
        Assert.True(sources.Count > 40);
        foreach (var source in sources)
        {
            var error = Record.Exception(() => renderer.Render(source));
            Assert.True(error is null, $"{source.Path}: {error}");
        }
    }
}
