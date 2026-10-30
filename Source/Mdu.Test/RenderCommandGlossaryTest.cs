using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class RenderCommandGlossaryTest
{
    [Fact]
    public void ReportsInvalidAttributionAsSiteConfigurationError()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalError = Console.Error;
        using var errors = new StringWriter();
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "attribution: false\n");
            var command = new RenderCommand(
                new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            Console.SetError(errors);
            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = Path.Combine(root, "site"),
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            Assert.Equal(1, result);
            Assert.Contains("Could not read site configuration or glossary: The 'attribution' setting", errors.ToString());
            Assert.False(Directory.Exists(Path.Combine(root, "site")));
        }
        finally
        {
            Console.SetError(originalError);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RendersSharedAbbreviationsAndExcludesGlossaryFromSiteAndSearch()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var glossaryDirectory = Path.Combine(root, "includes");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(glossaryDirectory);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\nAPI\n");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "glossary: includes/abbreviations.md\n");
        File.WriteAllText(Path.Combine(glossaryDirectory, "abbreviations.md"),
            "*[API]: Application Programming Interface\n");

        try
        {
            var command = new RenderCommand(
                new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = output,
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            Assert.Equal(0, result);
            var html = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains("<abbr title=\"Application Programming Interface\">API</abbr>", html);
            Assert.Contains("abbreviationTooltip.className = \"footnote-preview\"", html);
            Assert.DoesNotContain("abbreviations.html", Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories)
                .Select(Path.GetFileName));
            Assert.DoesNotContain("Application Programming Interface", File.ReadAllText(Path.Combine(output, "search-index.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
