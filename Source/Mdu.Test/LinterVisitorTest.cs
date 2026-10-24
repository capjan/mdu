using Markdown.Linter;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class LinterVisitorTest
{
    [Fact]
    public void MissingTitleHasStableDiagnosticCode()
    {
        WithPage("Overview.md", "Plain text without a heading", string.Empty, 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.HeadingRequired, CodeOf(error));
        });
    }

    [Fact]
    public void ExistingTitlePasses()
    {
        WithPage("Overview.md", "# Overview", "Overview", 0, errors => Assert.Empty(errors));
    }

    [Fact]
    public void UnusedFootnoteHasStableWarningCodeAndSourceLine()
    {
        WithPage("Overview.md", "# Overview\n\n[^unused]: Not referenced.", "Overview", 0,
            errors => Assert.Empty(errors),
            assertWarnings: warnings =>
            {
                var warning = Assert.Single(warnings);
                Assert.Equal(3, warning.LineNumber);
                Assert.Equal(RuleCodes.UnusedFootnote, CodeOf(warning));
            });
    }

    [Fact]
    public void ReferencedFootnoteDoesNotWarn()
    {
        WithPage("Overview.md", "# Overview\n\nA note[^used].\n\n[^used]: Referenced.", "Overview", 0,
            errors => Assert.Empty(errors), assertWarnings: Assert.Empty);
    }

    [Fact]
    public void MismatchedReadmeFolderHasStableDiagnosticCode()
    {
        WithPage(Path.Combine("wrong-folder", "README.md"), "# Installation", "Installation", 1, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.DirectoryTitleMatch, CodeOf(error));
        });
    }

    [Fact]
    public void MissingLocalLinkHasStableDiagnosticCodeAndSourceLine()
    {
        WithPage("Overview.md", "# Overview\n\n[Missing](missing.md)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(3, error.LineNumber);
            Assert.Equal(RuleCodes.LocalLinkExists, CodeOf(error));
        });
    }

    [Fact]
    public void MissingImageHasStableDiagnosticCode()
    {
        WithPage("Overview.md", "# Overview\n\n![Missing](missing.png)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.LocalLinkExists, CodeOf(error));
        });
    }

    [Fact]
    public void MissingPercentEncodedLocalLinkIsReported()
    {
        WithPage("Overview.md", "# Overview\n\n[Missing](missing%20file.v1.pdf)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(3, error.LineNumber);
            Assert.Equal(RuleCodes.LocalLinkExists, CodeOf(error));
        });
    }

    [Fact]
    public void ExistingImagePasses()
    {
        WithPage("Overview.md", "# Overview\n\n![Diagram](diagram.png)", "Overview", 0, errors => Assert.Empty(errors),
            setup: root => File.WriteAllText(Path.Combine(root, "diagram.png"), "image"));
    }

    [Fact]
    public void OldSidebarFrontMatterIsRejected()
    {
        const string frontMatter = "---\nsidebar:\n  order: 1\n---";
        WithPage("Overview.md", $"{frontMatter}\n\n# Overview", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.LegacySidebarMetadata, CodeOf(error));
        }, frontMatter: frontMatter);
    }

    [Fact]
    public void TopPlacementMustBeOnTheFirstLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Area", "Nested", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# Nested\n");
        try
        {
            var page = new MarkdownFile(new FileInfo(path), null)
            {
                Title = "Nested",
                HeadingTitle = "Nested",
                NavigationPlacement = NavigationPlacement.Top
            };
            var receiver = new RecordingLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            linter.Prepare(Array.Empty<MarkdownFile>(), root);
            linter.Process(page, 0);

            Assert.Contains(receiver.Errors, error => CodeOf(error) == RuleCodes.NavigationPlacementLevel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnknownNavigationPlacementIsRejected()
    {
        WithPage("Overview.md", "# Overview", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.InvalidNavigationPlacement, CodeOf(error));
        }, frontMatter: "---\nnavigation:\n  placement: unknown\n---", invalidNavigationPlacement: "unknown");
    }

    [Fact]
    public void ExpandedIsRejectedOnTopNavigationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Area", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# Area\n");
        try
        {
            var page = new MarkdownFile(new FileInfo(path), null)
            {
                Title = "Area",
                HeadingTitle = "Area",
                NavigationPlacement = NavigationPlacement.Top,
                NavigationExpanded = false
            };
            var receiver = new RecordingLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            linter.Prepare(Array.Empty<MarkdownFile>(), root);
            linter.Process(page, 0);

            Assert.Contains(receiver.Errors, error => CodeOf(error) == RuleCodes.ExpandedTopNavigationRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CasingMismatchDoesNotAlsoReportMissingTarget()
    {
        WithPage(Path.Combine("docs", "Overview.md"), "# Overview\n\n[Target](Target.md)", "Overview", 0, errors =>
        {
            Assert.Contains(errors, error => CodeOf(error) == RuleCodes.LinkCasing);
            Assert.DoesNotContain(errors, error => CodeOf(error) == RuleCodes.LocalLinkExists);
        }, setup: root =>
        {
            var docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, "target.md"), "# Target");
        });
    }

    private static string CodeOf(ReceivedError error) =>
        error.Message.Split(':', 2)[0].Split(' ', 2)[0];

    private static void WithPage(
        string relativePath,
        string contents,
        string title,
        int depth,
        Action<List<ReceivedError>> assert,
        Action<string>? setup = null,
        string frontMatter = "",
        string? invalidNavigationPlacement = null,
        Action<List<ReceivedError>>? assertWarnings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            setup?.Invoke(root);
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            var page = new MarkdownFile(new FileInfo(path), null)
            {
                Title = title,
                FrontMatter = frontMatter,
                InvalidNavigationPlacement = invalidNavigationPlacement
            };
            var receiver = new RecordingLintErrorReceiver();

            new LinterVisitor(receiver).Process(page, depth);

            assert(receiver.Errors);
            assertWarnings?.Invoke(receiver.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record ReceivedError(string FilePath, int LineNumber, string Message);

    private sealed class RecordingLintErrorReceiver : ILinterErrorReceiver
    {
        public List<ReceivedError> Errors { get; } = new();
        public List<ReceivedError> Warnings { get; } = new();
        public IReadOnlyList<LintDiagnostic> Diagnostics => Errors
            .Select(error => new LintDiagnostic(error.FilePath, error.LineNumber, error.Message, IsWarning: false))
            .Concat(Warnings.Select(warning => new LintDiagnostic(warning.FilePath, warning.LineNumber, warning.Message, IsWarning: true)))
            .ToArray();
        public int ErrorCount => Errors.Count;
        public int WarningCount => Warnings.Count;

        public void Add(string filePath, int lineNumber, string message) =>
            Errors.Add(new ReceivedError(filePath, lineNumber, message));

        public void AddWarning(string filePath, int lineNumber, string message) =>
            Warnings.Add(new ReceivedError(filePath, lineNumber, message));
    }
}
