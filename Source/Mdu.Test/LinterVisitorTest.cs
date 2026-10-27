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
                Assert.Contains("Footnote '[^unused]' is defined but never referenced.", warning.Message);
            });
    }

    [Fact]
    public void ReferencedFootnoteDoesNotWarn()
    {
        WithPage("Overview.md", "# Overview\n\nA note[^used].\n\n[^used]: Referenced.", "Overview", 0,
            errors => Assert.Empty(errors), assertWarnings: Assert.Empty);
    }

    [Fact]
    public void FootnotesReferencedOnlyByUnusedFootnotesAreUnused()
    {
        WithPage("Overview.md", "# Overview\n\n[^outer]: References [^inner].\n\n[^inner]: Inner note.", "Overview", 0,
            errors => Assert.Empty(errors),
            assertWarnings: warnings =>
            {
                Assert.Equal(2, warnings.Count);
                Assert.All(warnings, warning => Assert.Equal(RuleCodes.UnusedFootnote, CodeOf(warning)));
                Assert.Equal([3, 5], warnings.Select(warning => warning.LineNumber));
            });
    }

    [Fact]
    public void FootnotesReachableThroughUsedFootnotesDoNotWarn()
    {
        WithPage("Overview.md", "# Overview\n\nA note[^outer].\n\n[^outer]: References [^inner].\n\n[^inner]: Inner note.", "Overview", 0,
            errors => Assert.Empty(errors), assertWarnings: Assert.Empty);
    }

    [Fact]
    public void DuplicateFootnoteDefinitionHasStableWarningCodeAndSourceLine()
    {
        WithPage("Overview.md", "# Overview\n\nA note[^used].\n\n[^used]: Original.\n\n[^used]: Duplicate.", "Overview", 0,
            errors => Assert.Empty(errors),
            assertWarnings: warnings =>
            {
                var warning = Assert.Single(warnings);
                Assert.Equal(7, warning.LineNumber);
                Assert.Equal(RuleCodes.DuplicateFootnote, CodeOf(warning));
                Assert.Contains("Footnote '[^used]' is defined more than once", warning.Message);
            });
    }

    [Fact]
    public void DuplicateHeadingTitlesAtTheSameNavigationLevelAreRejected()
    {
        WithGraph(root =>
        {
            var first = CreateGraphPage(root, "TopicA.md", "Topic A");
            var duplicate = CreateGraphPage(root, "TopicA-copy.md", "Topic A");

            var error = Assert.Single(PrepareGraph(root, first, duplicate).Errors);

            Assert.Equal(RuleCodes.DuplicateNavigationTitle, CodeOf(error));
            Assert.Equal(duplicate.FileInfo.FullName, error.FilePath);
            Assert.Contains("Topic A", error.Message);
            Assert.Contains("TopicA.md", error.Message);
        });
    }

    [Fact]
    public void SameTitleUnderDifferentVisibleParentsPasses()
    {
        WithGraph(root =>
        {
            var guide = CreateGraphPage(root, Path.Combine("Guide", "README.md"), "Guide");
            var guideOverview = CreateGraphPage(root, Path.Combine("Guide", "Overview.md"), "Overview");
            var reference = CreateGraphPage(root, Path.Combine("Reference", "README.md"), "Reference");
            var referenceOverview = CreateGraphPage(root, Path.Combine("Reference", "Overview.md"), "Overview");
            guide.Children = [guideOverview];
            guideOverview.Parent = guide;
            reference.Children = [referenceOverview];
            referenceOverview.Parent = reference;

            var receiver = PrepareGraph(root, guide, reference);

            Assert.Empty(receiver.Errors);
        });
    }

    [Fact]
    public void DuplicateTitleErrorFollowsNavigationOrder()
    {
        WithGraph(root =>
        {
            var first = CreateGraphPage(root, "Zulu.md", "Shared", navigationOrder: 1);
            var duplicate = CreateGraphPage(root, "Alpha.md", "Shared", navigationOrder: 2);

            var error = Assert.Single(PrepareGraph(root, duplicate, first).Errors);

            Assert.Equal(duplicate.FileInfo.FullName, error.FilePath);
        });
    }

    [Fact]
    public void DuplicateNavigationTitlesAreRejectedEvenWhenHeadingsDiffer()
    {
        WithGraph(root =>
        {
            var first = CreateGraphPage(root, "First.md", "First heading", "Getting started");
            var duplicate = CreateGraphPage(root, "Second.md", "Second heading", "GETTING STARTED");

            var error = Assert.Single(PrepareGraph(root, first, duplicate).Errors);

            Assert.Equal(RuleCodes.DuplicateNavigationTitle, CodeOf(error));
        });
    }

    [Fact]
    public void DuplicateIconOnlyNavigationTitlesHaveHelpfulMessage()
    {
        WithGraph(root =>
        {
            var first = CreateGraphPage(root, "First.md", "First", ":material-book:");
            var duplicate = CreateGraphPage(root, "Second.md", "Second", ":fontawesome-book:");

            var error = Assert.Single(PrepareGraph(root, first, duplicate).Errors);

            Assert.Equal(RuleCodes.DuplicateNavigationTitle, CodeOf(error));
            Assert.Contains("Navigation title has no visible text", error.Message);
            Assert.Contains("Add text to navigation.title", error.Message);
            Assert.DoesNotContain("''", error.Message);
        });
    }

    [Fact]
    public void SameNavigationTitleAtDifferentLevelsPasses()
    {
        WithGraph(root =>
        {
            var parent = CreateGraphPage(root, "Guide/README.md", "Guide");
            var child = CreateGraphPage(root, Path.Combine("Guide", "Guide.md"), "Guide");
            parent.Children = [child];
            child.Parent = parent;

            var receiver = PrepareGraph(root, parent);

            Assert.Empty(receiver.Errors);
        });
    }

    [Fact]
    public void HiddenPagesAreExcludedFromDuplicateCheck()
    {
        WithGraph(root =>
        {
            var hiddenPage = CreateGraphPage(root, "HiddenTopic.md", "Topic", visible: false);
            var visiblePage = CreateGraphPage(root, "Topic.md", "Topic");

            var receiver = PrepareGraph(root, hiddenPage, visiblePage);

            Assert.Empty(receiver.Errors);
        });
    }

    [Fact]
    public void HiddenParentsDoNotAddANavigationLevelForChildren()
    {
        WithGraph(root =>
        {
            var hiddenSection = CreateGraphPage(root, Path.Combine("Hidden", "README.md"), "Hidden", visible: false);
            var promotedPage = CreateGraphPage(root, Path.Combine("Hidden", "Topic.md"), "Topic");
            var visiblePage = CreateGraphPage(root, "Topic.md", "Topic");
            hiddenSection.Children = [promotedPage];
            promotedPage.Parent = hiddenSection;

            var error = Assert.Single(PrepareGraph(root, hiddenSection, visiblePage).Errors);

            Assert.Equal(RuleCodes.DuplicateNavigationTitle, CodeOf(error));
        });
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

    [Theory]
    [InlineData("[Setup](#setup)")]
    [InlineData("[Security](#6-security)")]
    [InlineData("[Custom](#custom-id)")]
    [InlineData("[Raw](#raw-anchor)")]
    [InlineData("[Upper](#upper-anchor)")]
    [InlineData("[Legacy](#legacy-name)")]
    [InlineData("[Note](#fn:1)")]
    [InlineData("[Top](#top)")]
    [InlineData("[Text](#:~:text=Setup)")]
    [InlineData("[Mixed](#SETUP)")]
    public void SamePageFragmentsOfRenderedAnchorsPass(string link)
    {
        WithPage("Overview.md",
            $"# Overview\n\n{link}\n\n## Setup\n\n## 6. Security\n\n## Named {{#custom-id}}\n\n<a id=\"raw-anchor\"></a>\n\n<a ID=upper-anchor></a> <a title=\"x\" NAME='legacy-name'></a>\n\nA note[^1].\n\n[^1]: Note.",
            "Overview", 0, errors => Assert.Empty(errors));
    }

    [Fact]
    public void FormFieldNamesAndAttributeTextAreNoAnchors()
    {
        WithPage("Overview.md",
            "# Overview\n\n[Email](#email)\n\n[Title](#hidden)\n\n<input name=\"email\">\n\n<a title=\"id=hidden\"></a>",
            "Overview", 0, errors =>
            {
                Assert.Equal(2, errors.Count);
                Assert.All(errors, error => Assert.Equal(RuleCodes.LocalFragmentExists, CodeOf(error)));
            });
    }

    [Fact]
    public void MissingSamePageFragmentHasStableDiagnosticCodeAndSourceLine()
    {
        WithPage("Overview.md", "# Overview\n\n## Setup\n\n[Install](#install)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(5, error.LineNumber);
            Assert.Equal(RuleCodes.LocalFragmentExists, CodeOf(error));
            Assert.Contains("Link fragment '#install' does not match a heading or anchor on this page.", error.Message);
        });
    }

    [Fact]
    public void FragmentOnAnotherPageIsChecked()
    {
        WithPage(Path.Combine("docs", "Overview.md"),
            "# Overview\n\n[Found](Guide/Setup.md#3-install)\n\n[Missing](Guide/Setup.md#uninstall)\n\n[Section](Guide/#guide)",
            "Overview", 0, errors =>
            {
                var error = Assert.Single(errors);
                Assert.Equal(5, error.LineNumber);
                Assert.Equal(RuleCodes.LocalFragmentExists, CodeOf(error));
                Assert.Contains("'#uninstall' does not match a heading or anchor in 'Guide/Setup.md'", error.Message);
            }, setup: root =>
            {
                var guide = Path.Combine(root, "docs", "Guide");
                Directory.CreateDirectory(guide);
                File.WriteAllText(Path.Combine(guide, "README.md"), "# Guide");
                File.WriteAllText(Path.Combine(guide, "Setup.md"), "---\ntitle: Setup\n---\n# Setup\n\n## 3. Install");
            });
    }

    [Fact]
    public void FragmentOnAnotherPageMustMatchLetterCase()
    {
        WithPage("Overview.md", "# Overview\n\n[Context](Api.md#app.authorization.AuthorizedAuditContext)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.LocalFragmentExists, CodeOf(error));
            Assert.Contains("letter case of heading id 'app.authorization.authorizedauditcontext' in 'Api.md'", error.Message);
        }, setup: root => File.WriteAllText(Path.Combine(root, "Api.md"),
            "# API\n\n## `app.authorization.AuthorizedAuditContext`"));
    }

    [Fact]
    public void FragmentsOfMissingFilesAndOtherFilesAreNotChecked()
    {
        WithPage("Overview.md", "# Overview\n\n[Missing](missing.md#setup)\n\n[Site](site/index.html#setup)", "Overview", 0, errors =>
        {
            var error = Assert.Single(errors);
            Assert.Equal(RuleCodes.LocalLinkExists, CodeOf(error));
        }, setup: root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "site"));
            File.WriteAllText(Path.Combine(root, "site", "index.html"), "<html></html>");
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

    private static void WithGraph(Action<string> assert)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            assert(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MarkdownFile CreateGraphPage(
        string root,
        string relativePath,
        string title,
        string navigationTitle = "",
        bool visible = true,
        int? navigationOrder = null)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"# {title}\n");
        return new MarkdownFile(new FileInfo(path), null)
        {
            Title = title,
            HeadingTitle = title,
            NavigationTitle = navigationTitle,
            NavigationVisible = visible,
            NavigationOrder = navigationOrder,
            SiteRelativePath = Path.GetRelativePath(root, path)
        };
    }

    private static RecordingLintErrorReceiver PrepareGraph(string root, params MarkdownFile[] pages)
    {
        var receiver = new RecordingLintErrorReceiver();
        new LinterVisitor(receiver).Prepare(pages, root);
        return receiver;
    }

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
