using Markdown.Confluence;
using Markdown.Document.Impl;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

[Collection("Process state")]
public class ConfluenceCommandTest
{
    [Theory]
    [InlineData("title: dashboard", false)]
    [InlineData("description: dashboard layout examples", false)]
    [InlineData("layout: article\ndashboard:\n  clock: true", false)]
    [InlineData("layout: dashboard", true)]
    [InlineData("layout: DASHBOARD", true)]
    public void WarnsOnlyForActualDashboardLayout(string metadata, bool warns)
    {
        var root = Path.GetTempPath();
        var page = new MarkdownFile(new FileInfo(Path.Combine(root, "README.md")), null)
        {
            MarkdownContent = $"---\n{metadata}\n---\n# Home",
            FrontMatter = $"---\n{metadata}\n---",
            Title = "Home"
        };
        var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            ConfluenceCommand.PrepareSources([page], root);
        }
        finally { Console.SetOut(original); }
        Assert.Equal(warns, output.ToString().Contains("dashboard layout is not exported", StringComparison.Ordinal));
    }

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
    public void PreservesImportMarkerLinksAndDiagramAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-confluence-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "docs");
        var repository = Path.Combine(root, "repository");
        var importedDocs = Path.Combine(repository, "Docs");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(importedDocs);
        try
        {
            const string url = "https://github.com/example/repository/tree/main/Docs";
            File.WriteAllText(Path.Combine(docs, "README.md"), """
                # Home

                [Nested Markdown](Nested.md#nested)

                [Nested HTML](Nested.html#nested)

                ```mermaid
                flowchart LR
                A --> B
                click A href "Nested.html#nested"
                ```
                """);
            File.WriteAllText(Path.Combine(docs, "Nested.md"), $"---\nmdu:\n  import:\n    source: {url}\n---\n");
            File.WriteAllText(Path.Combine(importedDocs, "README.md"), "# Nested");
            var resolved = new ResolvedGitTreeSource("https://github.com/example/repository.git",
                "https://github.com/example/repository", "main", true, "Docs", repository, importedDocs,
                "https://github.com/example/repository/edit/main");
            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()), new LocalSourceResolver(resolved))
                .CreateGraph(new VisitorSettings { RootPath = docs, IncludeHidden = true, SearchPattern = "*.md" });
            var sources = ConfluenceCommand.PrepareSources(graph, docs);
            var target = new ConfluenceTarget("https://wiki.example.test", "DOC");
            var nested = sources.Single(source => source.Path == "Nested/README.md");
            var renderer = new ConfluenceStorageRenderer(target, sources);
            var result = renderer.ResolvePageLinks(renderer.Render(sources.Single(source => source.Path == "README.md")),
                new Dictionary<string, string> { ["README.md"] = "100", [nested.Path] = "101" });
            var xml = System.Xml.Linq.XElement.Parse("<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" +
                result.Storage + "</root>");
            System.Xml.Linq.XNamespace ri = "http://atlassian.com/resource/identifier";
            Assert.Equal(2, xml.Descendants(ri + "page").Count());
            Assert.All(xml.Descendants(ri + "page"), page => Assert.Equal(target.PageTitle(nested), page.Attribute(ri + "content-title")?.Value));
            Assert.Contains("pages/viewpage.action?pageId=101#",
                System.Text.Encoding.UTF8.GetString(Assert.Single(result.Assets).Content));
            Assert.Contains("mdu:", File.ReadAllText(Path.Combine(docs, "Nested.md")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class LocalSourceResolver(ResolvedGitTreeSource source) : IGitTreeSourceResolver
    {
        public ResolvedGitTreeSource Resolve(string sourceUrl) => source;
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
