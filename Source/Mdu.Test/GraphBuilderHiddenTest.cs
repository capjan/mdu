using Markdown.Document.Impl;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class GraphBuilderHiddenTest
{
    [Theory]
    [InlineData(false, new[] { "README.md" })]
    [InlineData(true, new[] { ".dot.md", "README.md", "README.md" })]
    public void HiddenFlagAppliesToFilesAndFolders(bool includeHidden, string[] expected)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var hiddenDirectory = Path.Combine(root, ".secret");
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
            var hiddenFile = Path.Combine(root, ".dot.md");
            File.WriteAllText(hiddenFile, "# Dot\n");
            File.WriteAllText(Path.Combine(hiddenDirectory, "README.md"), "# Secret\n");
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(hiddenDirectory, File.GetAttributes(hiddenDirectory) | FileAttributes.Hidden);
                File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
            }

            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()))
                .CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = includeHidden });

            var names = Flatten(graph).Select(page => page.FileInfo.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, names);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConfiguredGlossaryIsExcludedFromThePageGraph()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
            File.WriteAllText(Path.Combine(root, "Terms.md"), "*[API]: Application Programming Interface\n");
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "content.glossary: Terms.md\n");

            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()))
                .CreateGraph(
                    new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true },
                    SiteConfigurationReader.TryResolveGlossaryPath(root));

            Assert.Equal(["README.md"], Flatten(graph).Select(page => page.FileInfo.Name));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GraphBuilderDoesNotRequireValidSiteConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "content.glossary: [invalid\n");

            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()))
                .CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(["README.md"], Flatten(graph).Select(page => page.FileInfo.Name));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages) =>
        pages.SelectMany(page => new[] { page }.Concat(Flatten(page.Children ?? [])));
}
