using Core.Collections.NodeGraph.Extensions;
using Markdown.Document.Impl;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;
using Markdown.Renderer;
using Markdown.Search;

namespace Mdu.Test;

public class FixCommandTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixesReadmeWithoutSubpagesAndKeepsLinksWorking(bool invalidHtmlAttribution)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guide");
        Directory.CreateDirectory(section);
        try
        {
            if (invalidHtmlAttribution)
                File.WriteAllText(Path.Combine(root, "mdu.yml"), "attribution: false\n");
            File.WriteAllText(Path.Combine(root, "README.md"),
                "# Docs\n\n[Page](./Guide/README.md#intro)\n\n[Folder](./Guide/)\n\n[Reference][guide]\n\n[guide]: ./Guide/README.md?view=full#intro\n");
            Directory.CreateDirectory(Path.Combine(root, "api"));
            File.WriteAllText(Path.Combine(root, "api", "orders.yaml"), "openapi: 3.1.0\n");
            File.WriteAllText(Path.Combine(section, "README.md"),
                "# Guide\n\n```openapi\n../api/orders.yaml\n```\n\n[Asset](./diagram.svg)\n\n[Home](../README.md)\n\n[Anchor](#intro)\n");
            File.WriteAllText(Path.Combine(section, "diagram.svg"), "<svg />");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings
            {
                RootPath = root,
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            var movedPage = Path.Combine(root, "Guide.md");
            Assert.Equal(0, result);
            Assert.True(File.Exists(movedPage));
            Assert.False(File.Exists(Path.Combine(section, "README.md")));
            Assert.True(File.Exists(Path.Combine(section, "diagram.svg")));

            var rootContent = File.ReadAllText(Path.Combine(root, "README.md"));
            Assert.Contains("[Page](Guide.md#intro)", rootContent);
            Assert.Contains("[Folder](Guide.md)", rootContent);
            Assert.Contains("[guide]: Guide.md?view=full#intro", rootContent);

            var movedContent = File.ReadAllText(movedPage);
            Assert.Contains("```openapi\napi/orders.yaml\n```", movedContent);
            Assert.Contains("[Asset](Guide/diagram.svg)", movedContent);
            Assert.Contains("[Home](README.md)", movedContent);
            Assert.Contains("[Anchor](#intro)", movedContent);

            var graph = CreateGraphBuilder().CreateGraph(new VisitorSettings
            {
                RootPath = root,
                SearchPattern = "*.md",
                IncludeHidden = true
            });
            var receiver = new ConsoleLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            linter.Prepare(graph);
            graph.Visit(linter);
            Assert.Equal(0, receiver.WarningCount);
            Assert.Equal(0, receiver.ErrorCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesMkDocsBlocksIndexesAndPagesOrdering()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guide");
        var nested = Path.Combine(section, "Deep");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n\n[Guide](Guide/index.md)\n");
            File.WriteAllText(Path.Combine(root, ".pages"), "arrange:\n  - README.md\n  - Guide\n");
            File.WriteAllText(Path.Combine(section, "index.md"), "---\ntitle: Overview\n---\n# Guide\n\n??? custom \"Details\"\n\n    Visible content\n\n=== \"First\"\n\n    Tab content\n");
            File.WriteAllText(Path.Combine(nested, "Topic.md"), "# Topic\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(0, result);
            Assert.False(File.Exists(Path.Combine(root, ".pages")));
            Assert.True(File.Exists(Path.Combine(section, "README.md")));
            Assert.False(File.Exists(Path.Combine(section, "index.md")));
            Assert.Contains("navigation:", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.Contains("order: 1", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.Contains("[!NOTE]", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.Contains("**Details**", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.DoesNotContain("**\"Details\"**", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.Contains("### First", File.ReadAllText(Path.Combine(section, "README.md")));
            Assert.Contains("[Guide](Guide/README.md)", File.ReadAllText(Path.Combine(root, "README.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNotMutateFilesWhenPagesFrontMatterIsMalformed()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guide");
        Directory.CreateDirectory(section);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(section, "index.md"), "---\ntitle: [invalid\n---\n# Guide\n");
            File.WriteAllText(Path.Combine(section, ".pages"), "title: Guide\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(1, result);
            Assert.True(File.Exists(Path.Combine(section, "index.md")));
            Assert.False(File.Exists(Path.Combine(section, "README.md")));
            Assert.True(File.Exists(Path.Combine(section, ".pages")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNotMutateFilesWhenAnIndexConflictsWithAReadme()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(root, "index.md"), "# Index\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(1, result);
            Assert.True(File.Exists(Path.Combine(root, "index.md")));
            Assert.True(File.Exists(Path.Combine(root, "README.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GraphBuilderAndLinterHandleReadmeAndIndexInTheSameFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(root, "index.md"), "# Index\n");

            var graph = CreateGraphBuilder().CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });
            var receiver = new ConsoleLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            linter.Prepare(graph);
            graph.Visit(linter);

            Assert.Contains(receiver.Diagnostics, diagnostic => diagnostic.IsWarning &&
                                                                  diagnostic.Message.StartsWith("MDU012:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingSectionReadmeIsAWarningForPlainMarkdownProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guides");
        Directory.CreateDirectory(section);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(section, "First.md"), "# First\n");
            File.WriteAllText(Path.Combine(section, "Second.md"), "# Second\n");

            var graph = CreateGraphBuilder().CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });
            var receiver = new ConsoleLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            linter.Prepare(graph);
            graph.Visit(linter);

            Assert.Equal(0, receiver.ErrorCount);
            Assert.Equal(1, receiver.WarningCount);
            Assert.Contains(receiver.Diagnostics, diagnostic => diagnostic.IsWarning &&
                                                                  diagnostic.Message.StartsWith("MDU013:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNotTreatReindexMarkdownAsIndexInPagesArrange()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guide");
        Directory.CreateDirectory(section);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(section, "README.md"), "# Guide\n");
            File.WriteAllText(Path.Combine(section, "reindex.md"), "# Reindex\n");
            File.WriteAllText(Path.Combine(section, ".pages"), "arrange:\n  - reindex.md\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(0, result);
            Assert.Contains("navigation:", File.ReadAllText(Path.Combine(section, "reindex.md")));
            Assert.Contains("order: 0", File.ReadAllText(Path.Combine(section, "reindex.md")));
            Assert.DoesNotContain("order:", File.ReadAllText(Path.Combine(section, "README.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RemovesMaterialAnnotationMarkersInsideConvertedAlerts()
    {
        const string markdown = "??? note \"Example\"\n\n    Text\n    { .annotate }\n    ```ts\n    const value = 1;\n    ```\n";

        var converted = MarkdownCompatibility.FixMarkdown(markdown);

        Assert.DoesNotContain("{ .annotate }", converted);
        Assert.Contains("const value = 1;", converted);
        Assert.Empty(MarkdownCompatibility.FindIssues(converted));
    }

    [Fact]
    public void SeparatesConsecutiveConvertedAlerts()
    {
        const string markdown = "!!! note \"First\"\n\n    First body\n\n!!! warning \"Second\"\n\n    Second body\n";

        var converted = MarkdownCompatibility.FixMarkdown(markdown);

        Assert.Contains("> First body\n\n> [!WARNING]", converted);
    }

    [Fact]
    public void PreservesLineEndingsWhenNoCompatibilitySyntaxIsPresent()
    {
        const string markdown = "# Guide\r\n\r\nText\r\n";

        Assert.Equal(markdown, MarkdownCompatibility.FixMarkdown(markdown));
    }

    [Fact]
    public void PreservesCrLfWhenConvertingMaterialSyntax()
    {
        const string markdown = "!!! note\r\n\r\n    Body\r\n";

        var converted = MarkdownCompatibility.FixMarkdown(markdown);

        Assert.Contains("\r\n", converted);
        Assert.DoesNotContain("\n", converted.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotConvertCardGridExamplesInsideFencedCodeAndMatchesNestedDivs()
    {
        const string markdown = "```html\n<div class=\"grid cards\" markdown>\n- [example](example.md)\n<div>nested</div>\n</div>\n```\n\n<div class=\"grid cards\" markdown>\n- [guide](guide.md)\n<div class=\"nested\">Description</div>\n</div>\n";

        var issues = MarkdownCompatibility.FindIssues(markdown);
        var converted = MarkdownCompatibility.FixMarkdown(markdown);

        Assert.Single(issues.Where(issue => issue.Code == Markdown.Linter.RuleCodes.MaterialCards));
        Assert.Contains("<div class=\"grid cards\" markdown>", converted);
        Assert.Contains("- [guide](guide.md) Description", converted);
        Assert.DoesNotContain("<div class=\"nested\">", converted);
    }

    [Fact]
    public void MigratesBlogAndTagIndexesToStaticLists()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var posts = Path.Combine(root, "blog", "posts");
        Directory.CreateDirectory(posts);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(root, "Tags.md"), "# Tags\n\n<!-- material/tags -->\n");
            File.WriteAllText(Path.Combine(root, "blog", "index.md"), "---\ntitle: Wiki Blog\n---\n# Blog\n");
            File.WriteAllText(Path.Combine(root, "blog", ".pages"), "title: Blog\narrange:\n  - index.md\n");
            File.WriteAllText(Path.Combine(posts, "First.md"), "---\ntitle: First post\ndate: 2026-01-01\ntags:\n  - Release\n---\n# First\n");
            File.WriteAllText(Path.Combine(posts, "Second.md"), "---\ntitle: Second post\ndate: 2026-01-02\n---\n# Second\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(0, result);
            var blogIndex = File.ReadAllText(Path.Combine(root, "blog", "README.md"));
            Assert.Contains("navigation:", blogIndex);
            Assert.Contains("title: Wiki Blog", blogIndex);
            Assert.Contains("title: Blog", blogIndex);
            Assert.Contains("order: 0", blogIndex);
            Assert.Contains("posts/First.md", blogIndex);
            Assert.Contains("posts/Second.md", blogIndex);
            Assert.DoesNotContain("posts/README.md", blogIndex);
            Assert.Contains("## Release", File.ReadAllText(Path.Combine(root, "Tags.md")));
            Assert.Contains("blog/posts/First.md", File.ReadAllText(Path.Combine(root, "Tags.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LeavesOrdinaryDirectoryTitleMismatchesForTheLinter()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var section = Path.Combine(root, "Guides");
        Directory.CreateDirectory(section);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
            File.WriteAllText(Path.Combine(section, "README.md"), "# Overview\n");
            File.WriteAllText(Path.Combine(section, "Topic.md"), "# Topic\n");

            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var result = command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });

            Assert.Equal(0, result);
            Assert.DoesNotContain("navigation:", File.ReadAllText(Path.Combine(section, "README.md")));

            var receiver = new ConsoleLintErrorReceiver();
            var graph = CreateGraphBuilder().CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true });
            var linter = new LinterVisitor(receiver);
            linter.Prepare(graph);
            graph.Visit(linter);
            Assert.Equal(1, receiver.ErrorCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsCompactNavigationTitleFrontMatter()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "---\nnavigation.title: .NET\n---\n# Overview\n");
        try
        {
            var page = new Mdu.Model.MarkdownFile(new FileInfo(path), null);
            var visitor = new DataPreparationVisitor(new MarkdownDocumentReader());

            visitor.Process(page, 0);

            Assert.Equal(".NET", page.NavigationTitle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsCompactNavigationTitleAlongsideNestedSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "---\nnavigation:\n  order: 1\nnavigation.title: .NET\n---\n# Overview\n");
        try
        {
            var page = new Mdu.Model.MarkdownFile(new FileInfo(path), null);
            var visitor = new DataPreparationVisitor(new MarkdownDocumentReader());

            visitor.Process(page, 0);

            Assert.Equal(".NET", page.NavigationTitle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsNavigationPlacementAndExpandedFrontMatter()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "---\nnavigation:\n  placement: top\n  expanded: false\n---\n# Overview\n");
        try
        {
            var page = new Mdu.Model.MarkdownFile(new FileInfo(path), null);
            var visitor = new DataPreparationVisitor(new MarkdownDocumentReader());

            visitor.Process(page, 0);

            Assert.Equal(NavigationPlacement.Top, page.NavigationPlacement);
            Assert.Equal(false, page.NavigationExpanded);
            Assert.Null(page.InvalidNavigationPlacement);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsMalformedSiteConfigurationWithoutCreatingOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "site_name: [broken\n");
        try
        {
            var command = new RenderCommand(
                CreateGraphBuilder(),
                new CountNodesVisitor<Mdu.Model.MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = output,
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            Assert.Equal(1, result);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ServeReportsMalformedSiteConfigurationBeforeStartingTheServer()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "site_name: [broken\n");
        try
        {
            var command = new ServeCommand(
                CreateGraphBuilder(),
                new DataPreparationVisitor(new MarkdownDocumentReader()),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = output,
                SearchPattern = "*.md",
                IncludeHidden = true
            }, 43210, noBrowser: true);

            Assert.Equal(1, result);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesSingleMkDocsAutoAppendGlossaryIdempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "includes"));
        var glossary = Path.Combine(root, "includes", "abbreviations.md");
        const string glossaryContent = "*[API]: Application Programming Interface\n";
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n\nAPI\n");
        File.WriteAllText(glossary, glossaryContent);
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            markdown_extensions:
              - pymdownx.snippets:
                  auto_append:
                    - includes/abbreviations.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));
            var settings = new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true };

            Assert.Equal(0, command.Execute(settings));
            var configuration = File.ReadAllText(Path.Combine(root, "mdu.yml"));
            Assert.Contains("glossary: 'includes/abbreviations.md'", configuration);
            Assert.Equal(glossaryContent, File.ReadAllText(glossary));

            Assert.Equal(0, command.Execute(settings));
            Assert.Equal(configuration, File.ReadAllText(Path.Combine(root, "mdu.yml")));
            var graph = CreateGraphBuilder().CreateGraph(settings, SiteConfigurationReader.TryResolveGlossaryPath(root));
            Assert.DoesNotContain(Flatten(graph), page => page.FileInfo.FullName == glossary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesMkDocsGlossaryUsingConfiguredBasePath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var glossaryDirectory = Path.Combine(root, "shared");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(glossaryDirectory);
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(glossaryDirectory, "abbreviations.md"), "*[API]: Application Programming Interface\n");
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            docs_dir: Docs
            markdown_extensions:
              pymdownx.snippets:
                base_path: !relative $config_dir/shared
                auto_append:
                  - abbreviations.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));

            Assert.Equal(0, command.Execute(new VisitorSettings
            {
                RootPath = docs,
                SearchPattern = "*.md",
                IncludeHidden = true
            }));

            Assert.Contains("glossary: '../shared/abbreviations.md'", File.ReadAllText(Path.Combine(docs, "mdu.yml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesMkDocsGlossaryIntoCommentOnlyMduConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "includes"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "includes", "terms.md"), "*[API]: Application Programming Interface\n");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "# Existing configuration\n# Keep this comment\n");
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            markdown_extensions:
              - pymdownx.snippets:
                  auto_append:
                    - includes/terms.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));

            Assert.Equal(0, command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true }));

            var configuration = File.ReadAllText(Path.Combine(root, "mdu.yml"));
            Assert.Contains("# Keep this comment", configuration);
            Assert.Equal("includes/terms.md", SiteConfigurationReader.Read(root).Glossary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratesMkDocsGlossaryIntoFlowStyleMduConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "includes"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "includes", "terms.md"), "*[API]: Application Programming Interface\n");
        File.WriteAllText(Path.Combine(root, "mdu.yml"), "{site_name: Docs}\n");
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            markdown_extensions:
              - pymdownx.snippets:
                  auto_append:
                    - includes/terms.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));

            Assert.Equal(0, command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true }));

            var configuration = SiteConfigurationReader.Read(root);
            Assert.Equal("Docs", configuration.SiteName);
            Assert.Equal("includes/terms.md", configuration.Glossary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LeavesFlowStyleMduConfigurationUnchangedWhenTrailingCommentContainsBraces()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "includes"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "includes", "terms.md"), "*[API]: Application Programming Interface\n");
        const string configuration = "{site_name: Docs} # {x}\n";
        File.WriteAllText(Path.Combine(root, "mdu.yml"), configuration);
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            markdown_extensions:
              - pymdownx.snippets:
                  auto_append:
                    - includes/terms.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));

            Assert.Equal(0, command.Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true }));
            Assert.Equal(configuration, File.ReadAllText(Path.Combine(root, "mdu.yml")));
            Assert.Null(SiteConfigurationReader.Read(root).Glossary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LeavesAmbiguousMkDocsGlossaryConfigurationUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var terms = Path.Combine(root, "terms");
        Directory.CreateDirectory(terms);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Docs\n");
        const string glossaryContent = "!!! note\n\n    Keep the source unchanged.\n";
        File.WriteAllText(Path.Combine(terms, "one.md"), glossaryContent);
        File.WriteAllText(Path.Combine(terms, "two.md"), glossaryContent);
        File.WriteAllText(Path.Combine(root, "mkdocs.yml"), """
            markdown_extensions:
              - pymdownx.snippets:
                  auto_append:
                    - terms/one.md
                    - terms/two.md
            """);
        try
        {
            var command = new FixCommand(CreateGraphBuilder(), new LinterVisitor(new ConsoleLintErrorReceiver()));

            Assert.Equal(0, command.Execute(new VisitorSettings
            {
                RootPath = root,
                SearchPattern = "*.md",
                IncludeHidden = true
            }));
            Assert.False(File.Exists(Path.Combine(root, "mdu.yml")));
            Assert.Equal(glossaryContent, File.ReadAllText(Path.Combine(terms, "one.md")));
            Assert.Equal(glossaryContent, File.ReadAllText(Path.Combine(terms, "two.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GraphBuilder CreateGraphBuilder() =>
        new(new DataPreparationVisitor(new MarkdownDocumentReader()));

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages) =>
        pages.SelectMany(page => new[] { page }.Concat(Flatten(page.Children ?? [])));
}
