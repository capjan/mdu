using Markdown.Document.Impl;
using Markdown.Linter;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class CodeAnnotationsCommandTest
{
    private const string AnnotatedPage = "```python\nx = 1 # (1)!\n```\n\n1. The answer.\n";

    [Theory]
    [InlineData("", true)]
    [InlineData("content.code.annotate: false", false)]
    [InlineData("content:\n  code:\n    annotate: false", false)]
    [InlineData("content.code:\n  annotate: true", true)]
    [InlineData("content:\n  code:\n    annotate: false\ncontent.code.annotate: False", false)]
    public void ReadsTheSiteSettingNestedOrDotted(string yaml, bool expected) => WithRoot(root =>
    {
        File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);

        Assert.Equal(expected, SiteConfigurationReader.Read(root).CodeAnnotations);
    });

    [Theory]
    [InlineData("content.code.annotate: maybe", "The 'content.code.annotate' setting in mdu.yml must be true or false.")]
    [InlineData("content.code.wrap: true", "Unknown setting 'content.code.wrap' in mdu.yml.")]
    [InlineData("content:\n  code:\n    annotate: false\ncontent.code.annotate: true", "The 'content.code.annotate' setting in mdu.yml has conflicting nested and compact values.")]
    public void RejectsInvalidSiteSettings(string yaml, string message) => WithRoot(root =>
    {
        File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);

        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(root)).Message);
    });

    [Fact]
    public void RenderAppliesTheSiteSettingAndPagesOverrideIt() => WithRoot(root =>
    {
        var output = Path.Combine(root, "site");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "content:\n  code:\n    annotate: false\n");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\n" + AnnotatedPage);
        File.WriteAllText(Path.Combine(root, "On.md"), "---\ncontent.code.annotate: true\n---\n# On\n\n" + AnnotatedPage);

        var result = new RenderCommand(
            new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
            new CountNodesVisitor<MarkdownFile>(),
            new HtmlRenderer(),
            new SearchIndexBuilder()).Execute(new RenderCommandSettings
        {
            RootPath = root,
            OutputPath = output,
            SearchPattern = "*.md",
            IncludeHidden = true
        });

        Assert.Equal(0, result);
        Assert.DoesNotContain("class=\"code-annotation ", File.ReadAllText(Path.Combine(output, "index.html")));
        Assert.Contains("class=\"code-annotation ", File.ReadAllText(Path.Combine(output, "On.html")));
    });

    [Fact]
    public void LintReportsMarkersWithoutEntriesEntriesWithoutMarkersAndInvalidSettings() => WithRoot(root =>
    {
        var path = Path.Combine(root, "Page.md");
        File.WriteAllText(path, "# Page\n\n```python\na = 1 # (1)\nb = 2 # (3)\n```\n\n1. One\n2. Two\n\n```python annotate=yes\nc\n```\n");
        var receiver = new Receiver();
        var visitor = new LinterVisitor(receiver);
        visitor.Prepare([], root);

        visitor.Process(new MarkdownFile(new FileInfo(path), null) { Title = "Page", FrontMatter = "---\ncontent.code.annotate: maybe\n---" }, 0);

        Assert.Contains(receiver.Warnings, warning => warning.StartsWith($"3:{RuleCodes.AnnotationWithoutEntry}: Code annotation (3)", StringComparison.Ordinal));
        Assert.Contains(receiver.Warnings, warning => warning.StartsWith($"9:{RuleCodes.AnnotationEntryUnused}: Entry 2 ", StringComparison.Ordinal));
        Assert.Contains($"1:{RuleCodes.InvalidCodeAnnotate}: content.code.annotate must be 'true' or 'false', not 'maybe'.", receiver.Errors);
        Assert.Contains(receiver.Errors, error => error.StartsWith($"11:{RuleCodes.InvalidCodeAnnotate}: annotate=", StringComparison.Ordinal));
    });

    [Fact]
    public void LintSkipsBlocksWhereAnnotationsAreOff() => WithRoot(root =>
    {
        var path = Path.Combine(root, "Page.md");
        File.WriteAllText(path, "# Page\n\n```python annotate=false\na = 1 # (1)\n```\n\n1. One\n2. Two\n");
        var receiver = new Receiver();
        var visitor = new LinterVisitor(receiver);
        visitor.Prepare([], root);

        visitor.Process(new MarkdownFile(new FileInfo(path), null) { Title = "Page" }, 0);

        Assert.Empty(receiver.Warnings);
        Assert.Empty(receiver.Errors);
    });

    [Theory]
    [InlineData("``` { .yaml .annotate }\nkey: 1 # (1)\n```", "```yaml\nkey: 1 # (1)\n```")]
    [InlineData("```{.py .annotate title=\"x.py\"}\nx\n```", "```py title=\"x.py\"\nx\n```")]
    [InlineData("> ~~~ { .annotate .bash }\n> ls\n> ~~~", "> ~~~bash\n> ls\n> ~~~")]
    [InlineData("``` { .yaml #config }\nkey: 1\n```", "``` { .yaml #config }\nkey: 1\n```")]
    public void FixWritesAttributeFencesAsPlainLanguages(string markdown, string expected) =>
        Assert.Equal(expected, MarkdownCompatibility.FixMarkdown(markdown));

    [Fact]
    public void FixLeavesFencesInsideALongerFenceAlone()
    {
        const string markdown = "````markdown\n```\ntext\n```\n``` { .yaml .annotate }\nkey: 1\n```\n````";

        Assert.Equal(markdown, MarkdownCompatibility.FixMarkdown(markdown));
        Assert.Empty(MarkdownCompatibility.FindIssues(markdown));
    }

    [Fact]
    public void FixAndLintTreatAFenceWithAnInfoStringAsContent()
    {
        const string markdown = "```markdown\n```yaml\n{ .annotate }\n!!! note\n```\n";

        Assert.Equal(markdown, MarkdownCompatibility.FixMarkdown(markdown));
        Assert.Empty(MarkdownCompatibility.FindIssues(markdown));
    }

    [Theory]
    // With annotations the comment that only held the marker is gone: the code "x = 1", the raised number 1, then the
    // legend entry with its number bubble 1 and its text.
    [InlineData("", "x = 111The answer.")]
    [InlineData("content.code.annotate: false", "x = 1 # (1)!")]
    public void ThePdfFollowsTheSiteSetting(string yaml, string expectedText) => WithRoot(root =>
    {
        File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\n" + AnnotatedPage);
        var output = Path.Combine(root, "site.pdf");

        var result = new PdfCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
            new PdfRenderer(new HtmlRenderer())).Execute(new RenderCommandSettings { RootPath = root, OutputPath = output });

        Assert.Equal(0, result);
        // The text of all drawing operations in order, so it does not matter how the writer splits a line into runs.
        var pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output));
        var text = string.Concat(System.Text.RegularExpressions.Regex.Matches(pdf, "<(?<hex>[0-9A-F]*)> Tj")
            .Select(match => System.Text.Encoding.Latin1.GetString(Convert.FromHexString(match.Groups["hex"].Value))));
        Assert.Contains(expectedText, text);
    });

    [Fact]
    public void LintReportsAttributeFences()
    {
        var issue = Assert.Single(MarkdownCompatibility.FindIssues("# Page\n\n``` { .yaml .annotate }\nkey: 1\n```\n"));

        Assert.Equal((3, RuleCodes.CodeAnnotations), (issue.LineNumber, issue.Code));
        Assert.Contains("run 'mdu fix'", issue.Message);
    }

    [Fact]
    public void Mdu011ExplainsThatCodeAnnotationsWork() =>
        Assert.Contains("code annotations work", Assert.Single(MarkdownCompatibility.FindIssues("Text\n{ .annotate }\n")).Message);

    private static void WithRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-code-annotations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Receiver : ILinterErrorReceiver
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public IReadOnlyList<LintDiagnostic> Diagnostics => [];
        public int ErrorCount => Errors.Count;
        public int WarningCount => Warnings.Count;
        public void Add(string filePath, int lineNumber, string message) => Errors.Add($"{lineNumber}:{message}");
        public void AddWarning(string filePath, int lineNumber, string message) => Warnings.Add($"{lineNumber}:{message}");
    }
}
