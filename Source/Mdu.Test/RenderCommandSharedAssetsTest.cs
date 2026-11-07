using AngleSharp.Html.Parser;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

[Collection("Process state")]
public class RenderCommandSharedAssetsTest
{
    [Fact]
    public void LargeSiteSharesNavigationAndRuntimeWithinRawSizeBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-shared-assets-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "docs");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(Path.Combine(docs, "Nested folder"));
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Home\n");
        File.WriteAllText(Path.Combine(docs, "Nested folder", "README.md"), "# Section\n");
        for (var index = 0; index < 100; index++)
            File.WriteAllText(Path.Combine(docs, "Nested folder", $"Page {index}.md"),
                $"# Page {index}\n\n## Details\n\n```text\n  preserve   spaces\n    and indentation\n```\n");
        try
        {
            var command = new RenderCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            Assert.Equal(0, command.Execute(new RenderCommandSettings
            {
                RootPath = docs, OutputPath = output, SearchPattern = "*.md", IncludeHidden = true
            }));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
            var bytes = files.Sum(path => new FileInfo(path).Length);
            // Includes HTML, aliases, CSS, JS, navigation, sitemap and search. Duplicating the tree
            // or common scripts on 102 pages exceeds this budget by several megabytes.
            Assert.True(bytes < 2_000_000, $"Expected less than 2 MB of raw output, got {bytes:N0} bytes.");
            var parser = new HtmlParser();
            foreach (var path in files.Where(path => path.EndsWith(".html")))
            {
                var page = parser.ParseDocument(File.ReadAllText(path));
                if (page.QuerySelector(".site-sidebar") is null) continue; // aliases and the static sitemap
                Assert.Single(page.QuerySelectorAll(".site-sidebar .site-nav-link"));
                Assert.Contains("_mdu/sitemap.html", page.QuerySelector(".site-sidebar a")!.GetAttribute("href"));
                Assert.NotNull(page.QuerySelector(".page-meta a[href$='_mdu/sitemap.html']"));
                Assert.All(page.QuerySelectorAll("script"), script =>
                {
                    Assert.True(script.HasAttribute("src"), "Common runtime must not be repeated inline.");
                    var src = script.GetAttribute("src")!;
                    Assert.Matches(@"\?v=[0-9a-f]{12}$", src); // generated assets are cache-busted per content
                    var source = Uri.UnescapeDataString(src.Split('?')[0]);
                    Assert.True(File.Exists(Path.GetFullPath(source, Path.GetDirectoryName(path)!)), source);
                });
                Assert.Empty(page.QuerySelectorAll("style"));
                Assert.All(page.QuerySelectorAll("link[rel=stylesheet]"), link =>
                    Assert.Matches(@"\?v=[0-9a-f]{12}$", link.GetAttribute("href")!));
            }
            var sitemap = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "_mdu", "sitemap.html")));
            Assert.Equal(102, sitemap.QuerySelectorAll(".site-nav-link").Length);
            Assert.Empty(sitemap.QuerySelectorAll("[hidden], button"));
            Assert.Equal("auto", sitemap.DocumentElement.GetAttribute("data-theme")); // follows the reader's theme
            Assert.All(sitemap.QuerySelectorAll("a"), link => Assert.True(File.Exists(Path.GetFullPath(
                Uri.UnescapeDataString(link.GetAttribute("href")!), Path.Combine(output, "_mdu")))));
            var nested = File.ReadAllText(Path.Combine(output, "Nested folder", "Page 42.html"));
            Assert.Contains("data-current=\"Page%2042.html\"", nested);
            Assert.Contains("  preserve   spaces\n    and indentation", nested);
            Assert.Single(files.Where(path => path.EndsWith("/js/navigation.js", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("_mdu/sitemap.md", "# My sitemap\n", "# Home\n")]
    [InlineData("_mdu/js/theme.js", "// site script\n", "# Home\n\n[Script](_mdu/js/theme.js)\n")]
    public void PagesAndAssetsWrittenIntoTheReservedFolderAreRejected(string path, string content, string home)
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-reserved-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(docs, path))!);
        File.WriteAllText(Path.Combine(docs, "README.md"), home);
        File.WriteAllText(Path.Combine(docs, path), content);
        try
        {
            var (code, errors) = Render(docs, Path.Combine(root, "site"));
            Assert.NotEqual(0, code);
            Assert.Contains("reserved for files generated by mdu", errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceFolderNamedMduIsAllowedWhenNothingIsWrittenIntoIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-reserved-allowed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_mdu"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
        File.WriteAllText(Path.Combine(root, "_mdu", "notes.txt"), "unreferenced\n");
        try
        {
            // Rendering in place twice: the second run finds the _mdu folder written by the first.
            Assert.Equal(0, Render(root, root).Code);
            Assert.True(File.Exists(Path.Combine(root, "_mdu", "sitemap.html")));
            var (code, errors) = Render(root, root);
            Assert.True(code == 0, errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int Code, string Errors) Render(string docs, string output)
    {
        var error = Console.Error;
        using var captured = new StringWriter();
        try
        {
            Console.SetError(captured);
            var command = new RenderCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            var code = command.Execute(new RenderCommandSettings
            {
                RootPath = docs, OutputPath = output, SearchPattern = "*.md", IncludeHidden = true
            });
            return (code, captured.ToString());
        }
        finally
        {
            Console.SetError(error);
        }
    }
}
