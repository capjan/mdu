using Markdown.Document.Impl;
using Markdown.Renderer;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Service;
using Mdu.Service.Visitors;
using System.Text;

namespace Mdu.Test;

public class PdfCommandTest
{
    [Fact]
    public void SelectsOnlyThePageAndItsNavigationDescendants()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-pdf-subtree-{Guid.NewGuid():N}");
        var docs = Path.Combine(root, "docs");
        var topic = Path.Combine(docs, "Topic");
        Directory.CreateDirectory(Path.Combine(topic, "Deep"));
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Outside\n");
        File.WriteAllText(Path.Combine(topic, "README.md"), "# Selected\n\n[Details](Details.md#details)");
        File.WriteAllText(Path.Combine(topic, "Details.md"), "---\nnavigation.order: 1\n---\n# Details\n");
        File.WriteAllText(Path.Combine(topic, "Deep", "README.md"), "# Nested\n");
        File.WriteAllText(Path.Combine(topic, "Hidden.md"), "---\npdf.visible: false\n---\n# Invisible\n");
        var output = Path.Combine(root, "topic.pdf");
        try
        {
            Assert.Equal(0, CreateCommand().Execute(new RenderCommandSettings
            {
                RootPath = docs, OutputPath = output, PdfRootPage = "Topic/README.md"
            }));
            var pdf = File.ReadAllText(output, Encoding.Latin1);
            Assert.Contains(Convert.ToHexString(Encoding.Latin1.GetBytes("Selected")), pdf);
            Assert.Contains(Convert.ToHexString(Encoding.Latin1.GetBytes("Details")), pdf);
            Assert.Contains(Convert.ToHexString(Encoding.Latin1.GetBytes("Nested")), pdf);
            Assert.DoesNotContain(Convert.ToHexString(Encoding.Latin1.GetBytes("Outside")), pdf);
            Assert.DoesNotContain(Convert.ToHexString(Encoding.Latin1.GetBytes("Invisible")), pdf);
            Assert.Contains("/Subtype /Link", pdf);
            Assert.Contains("/Dest [", pdf);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Missing.md", false)]
    [InlineData("../Outside.md", false)]
    [InlineData("/README.md", false)]
    [InlineData("README.html", false)]
    [InlineData("", false)]
    [InlineData("README.md", true)]
    public void RejectsInvalidRootSelectionWithoutWritingPdf(string page, bool singleFile)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-pdf-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "README.md");
        File.WriteAllText(input, "# Home");
        var output = Path.Combine(root, "result.pdf");
        try
        {
            Assert.Equal(1, CreateCommand().Execute(new RenderCommandSettings
            {
                RootPath = singleFile ? input : root, OutputPath = output, PdfRootPage = page
            }));
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WritesOnePdfForTheSelectedMarkdownFolder()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"mdu-pdf-test-{Guid.NewGuid():N}");
        var docsPath = Path.Combine(rootPath, "Docs");
        var outputPath = Path.Combine(rootPath, "manual.pdf");
        Directory.CreateDirectory(docsPath);
        File.WriteAllText(Path.Combine(docsPath, "README.md"), "# Manual\n\nPDF output works.");

        try
        {
            var reader = new MarkdownDocumentReader();
            var graphBuilder = new GraphBuilder(new DataPreparationVisitor(reader));
            var renderer = new PdfRenderer(new HtmlRenderer());
            var command = new PdfCommand(graphBuilder, renderer);

            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = docsPath,
                OutputPath = outputPath,
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            Assert.Equal(0, result);
            Assert.True(File.Exists(outputPath));
            Assert.StartsWith("%PDF-1.7", File.ReadAllText(outputPath, System.Text.Encoding.Latin1));
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void RendersOneMarkdownFileWithoutFrontMatter()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"mdu-pdf-file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var pagePath = Path.Combine(rootPath, "Math.md");
        var outputPath = Path.Combine(rootPath, "math.pdf");
        File.WriteAllText(pagePath, "# Single page\n\nOnly this page should be rendered.");
        File.WriteAllText(Path.Combine(rootPath, "Other.md"), "# Other\n\nThis page is not selected.");

        try
        {
            var command = CreateCommand();
            var result = command.Execute(new RenderCommandSettings { RootPath = pagePath, OutputPath = outputPath });

            var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(outputPath));
            Assert.Equal(0, result);
            Assert.Contains("/Count 1", pdf);
            Assert.Contains("<53696E676C65> Tj", pdf);
            Assert.Contains("<70616765> Tj", pdf);
            Assert.DoesNotContain("<496E68616C74737665727A656963686E6973>", pdf);
            Assert.DoesNotContain("<4F74686572>", pdf);
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void OmitsPagesMarkedInvisibleInPdfFrontMatter()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"mdu-pdf-visibility-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var outputPath = Path.Combine(rootPath, "manual.pdf");
        File.WriteAllText(Path.Combine(rootPath, "README.md"), "# Manual\n\nIncluded page.");
        File.WriteAllText(Path.Combine(rootPath, "Flat.md"), "---\npdf.visible: false\n---\n# Hidden Flat\n");
        File.WriteAllText(Path.Combine(rootPath, "Nested.md"), "---\npdf:\n  visible: false\n---\n# Hidden Nested\n");

        try
        {
            var command = CreateCommand();
            var result = command.Execute(new RenderCommandSettings { RootPath = rootPath, OutputPath = outputPath });

            var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(outputPath));
            Assert.Equal(0, result);
            Assert.Contains("/Count 3", pdf);
            Assert.DoesNotContain("<48696464656E20466C6174>", pdf);
            Assert.DoesNotContain("<48696464656E204E6573746564>", pdf);
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    private static PdfCommand CreateCommand()
    {
        var reader = new MarkdownDocumentReader();
        var graphBuilder = new GraphBuilder(new DataPreparationVisitor(reader));
        return new PdfCommand(graphBuilder, new PdfRenderer(new HtmlRenderer()));
    }
}
