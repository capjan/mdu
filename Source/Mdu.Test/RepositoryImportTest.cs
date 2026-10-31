using System.Diagnostics;
using System.Text;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class RepositoryImportTest
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("index.md")]
    public void UsesMountedFolderForUntitledImportedPdfNamesAndTitles(string rootFileName)
    {
        var root = CreateTemporaryDirectory();
        var docs = Path.Combine(root, "docs");
        var repository = Path.Combine(root, "repository");
        var remoteDocs = Path.Combine(repository, "Docs");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(remoteDocs);
        const string sourceUrl = "https://github.com/example/api/tree/main/Docs";
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Home\n\n[Topic](mdu:pdf?root=API.md)");
        File.WriteAllText(Path.Combine(docs, "API.md"),
            $"---\ntitle: \"\"\nmdu:\n  import:\n    source: {sourceUrl}\n---\n");
        File.WriteAllText(Path.Combine(remoteDocs, rootFileName), "---\ntitle: \"\"\n---\nContent without a heading.");
        var builder = CreateGraphBuilder(new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "api"))));
        var site = Path.Combine(root, "site");
        try
        {
            var command = new RenderCommand(builder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            Assert.Equal(0, command.Execute(new RenderCommandSettings { RootPath = docs, OutputPath = site }));
            Assert.Contains("download=\"API.pdf\"", File.ReadAllText(Path.Combine(site, "index.html")));
            var pdf = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(site, "_mdu-pdf"), "*.pdf")), Encoding.Latin1);
            Assert.Contains("/Title (API)", pdf);
            Assert.DoesNotContain("/Title (Docs)", pdf);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("API.md", true)]
    [InlineData("API/index.md", true)]
    [InlineData("API/Details.md", false)]
    public void ExportsImportedNavigationSubtreesFromCliAndSiteLinks(string selectedPath, bool includesRoot)
    {
        var root = CreateTemporaryDirectory();
        var docs = Path.Combine(root, "docs");
        var repository = Path.Combine(root, "repository");
        var remoteDocs = Path.Combine(repository, "Docs");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(remoteDocs);
        const string sourceUrl = "https://github.com/example/api/tree/main/Docs";
        File.WriteAllText(Path.Combine(docs, "README.md"), $"# Outside\n\n[Topic](mdu:pdf?root={selectedPath})");
        File.WriteAllText(Path.Combine(docs, "API.md"), $"---\ntitle: Mounted\nmdu:\n  import:\n    source: {sourceUrl}\n---\n");
        File.WriteAllText(Path.Combine(remoteDocs, "index.md"), "# Remote\n\n[Self](mdu:pdf?root=index.md)\n");
        File.WriteAllText(Path.Combine(remoteDocs, "Details.md"), "# Details\n");
        var builder = CreateGraphBuilder(new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "api"))));
        var output = Path.Combine(root, "selected.pdf");
        try
        {
            Assert.Equal(0, new PdfCommand(builder, new PdfRenderer(new HtmlRenderer())).Execute(new RenderCommandSettings
            {
                RootPath = docs, OutputPath = output, PdfRootPage = selectedPath
            }));
            var pdf = File.ReadAllText(output, Encoding.Latin1);
            Assert.Equal(includesRoot, pdf.Contains(Convert.ToHexString(Encoding.Latin1.GetBytes("Remote")), StringComparison.Ordinal));
            Assert.Contains(Convert.ToHexString(Encoding.Latin1.GetBytes("Details")), pdf);
            Assert.DoesNotContain(Convert.ToHexString(Encoding.Latin1.GetBytes("Outside")), pdf);

            var site = Path.Combine(root, "site");
            var command = new RenderCommand(builder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            Assert.Equal(0, command.Execute(new RenderCommandSettings { RootPath = docs, OutputPath = site }));
            var generated = Directory.GetFiles(Path.Combine(site, "_mdu-pdf"), "*.pdf");
            Assert.Equal(includesRoot ? 1 : 2, generated.Length);
            Assert.False(File.Exists(Path.Combine(site, "documentation.pdf")));
            Assert.Contains("href=\"../_mdu-pdf/", File.ReadAllText(Path.Combine(site, "API", "index.html")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportsRepositoryTreeAndExpandsLocalChildImports()
    {
        var root = CreateTemporaryDirectory();
        var firstRepository = Path.Combine(root, "first-repository");
        var secondRepository = Path.Combine(root, "second-repository");
        var docsPath = Path.Combine(root, "docs");
        Directory.CreateDirectory(Path.Combine(firstRepository, "Docs"));
        Directory.CreateDirectory(Path.Combine(secondRepository, "Docs"));
        Directory.CreateDirectory(docsPath);
        var outerUrl = "https://github.com/example/first/tree/main/Docs";
        var nestedUrl = "https://github.com/example/second/tree/main/Docs";
        File.WriteAllText(Path.Combine(docsPath, "README.md"), $$"""
            ---
            title: Combined API
            mdu:
              import:
                source: {{outerUrl}}
            ---
            """);
        File.WriteAllText(Path.Combine(docsPath, "Nested.md"), $$"""
            ---
            title: Nested API
            mdu:
              import:
                source: {{nestedUrl}}
            ---
            """);
        File.WriteAllText(Path.Combine(firstRepository, "Docs", "README.md"), "# Remote API\n\nRemote introduction.\n");
        File.WriteAllText(Path.Combine(secondRepository, "Docs", "README.md"), "# Nested Remote API\n\nNested remote introduction.\n");

        try
        {
            var resolver = new FakeSourceResolver(
                (outerUrl, CreateResolvedSource(firstRepository, "first")),
                (nestedUrl, CreateResolvedSource(secondRepository, "second")));
            var graph = CreateGraphBuilder(resolver).CreateGraph(new VisitorSettings { RootPath = docsPath });

            var rootPage = Assert.Single(graph);
            Assert.Equal("Combined API", rootPage.Title);
            Assert.Contains("Remote introduction.", rootPage.ReadMarkdown());
            Assert.Equal("README.md", rootPage.SiteRelativePath);
            Assert.Equal("https://github.com/example/first/edit/main/Docs/README.md", rootPage.EditPageLink);

            var nestedPage = Assert.Single(rootPage.Children);
            Assert.Equal("Nested API", nestedPage.Title);
            Assert.Contains("Nested remote introduction.", nestedPage.ReadMarkdown());
            Assert.Equal(Path.Combine("Nested", "README.md"), nestedPage.SiteRelativePath);
            Assert.Equal(2, resolver.ResolveCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsMarkdownBodyOnImportMarker()
    {
        var root = CreateTemporaryDirectory();
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(Path.Combine(repository, "Docs"));
        var sourceUrl = "https://github.com/example/repository/tree/main/Docs";
        File.WriteAllText(Path.Combine(root, "README.md"), $"---\nmdu:\n  import:\n    source: {sourceUrl}\n---\nThis would be lost.\n");
        File.WriteAllText(Path.Combine(repository, "Docs", "README.md"), "# Imported\n");

        try
        {
            var resolver = new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "repository")));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateGraphBuilder(resolver).CreateGraph(new VisitorSettings { RootPath = root }));
            Assert.Contains("must contain front matter only", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AppliesImportedGlossaryToImportedPagesOnlyAndExcludesItFromTheSite()
    {
        var root = CreateTemporaryDirectory();
        var docs = Path.Combine(root, "docs");
        var architecture = Path.Combine(root, "architecture");
        var architectureDocs = Path.Combine(architecture, "Docs");
        var backend = Path.Combine(root, "backend");
        Directory.CreateDirectory(Path.Combine(docs, "_includes"));
        Directory.CreateDirectory(Path.Combine(docs, "Architecture"));
        Directory.CreateDirectory(Path.Combine(architectureDocs, "includes"));
        Directory.CreateDirectory(Path.Combine(backend, "Docs"));
        const string architectureUrl = "https://github.com/example/architecture/tree/main/Docs";
        const string backendUrl = "https://github.com/example/backend/tree/main/Docs";
        const string terms = "API, CI and ADR.";

        File.WriteAllText(Path.Combine(docs, "mdu.yml"), "glossary: _includes/abbreviations.md\n");
        File.WriteAllText(Path.Combine(docs, "_includes", "abbreviations.md"),
            "*[API]: Application Programming Interface\n*[CI]: Continuous Integration\n");
        File.WriteAllText(Path.Combine(docs, "README.md"), $"# Home\n\n{terms}\n");
        File.WriteAllText(Path.Combine(docs, "Architecture", "README.md"),
            $"---\ntitle: Architecture\nmdu:\n  import:\n    source: {architectureUrl}\n---\n");
        File.WriteAllText(Path.Combine(docs, "Architecture", "Local.md"), $"# Local\n\n{terms}\n");
        File.WriteAllText(Path.Combine(docs, "Backend.md"),
            $"---\ntitle: Backend\nmdu:\n  import:\n    source: {backendUrl}\n---\n");

        File.WriteAllText(Path.Combine(architectureDocs, "mdu.yml"), "glossary: includes/abbreviations.md\n");
        File.WriteAllText(Path.Combine(architectureDocs, "includes", "abbreviations.md"),
            "*[CI]: Configuration Item\n*[ADR]: Architecture Decision Record\n");
        File.WriteAllText(Path.Combine(architectureDocs, "includes", "Snippets.md"), "# Snippets\n");
        File.WriteAllText(Path.Combine(architectureDocs, "README.md"), $"# Architecture\n\n{terms}\n");
        File.WriteAllText(Path.Combine(backend, "Docs", "README.md"), $"# Backend\n\n{terms}\n");

        var site = Path.Combine(root, "site");
        try
        {
            var builder = CreateGraphBuilder(new FakeSourceResolver(
                (architectureUrl, CreateResolvedSource(architecture, "architecture")),
                (backendUrl, CreateResolvedSource(backend, "backend"))));
            var command = new RenderCommand(builder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());

            Assert.Equal(0, command.Execute(new RenderCommandSettings { RootPath = docs, OutputPath = site }));

            Assert.False(File.Exists(Path.Combine(site, "Architecture", "includes", "abbreviations.html")));
            Assert.True(File.Exists(Path.Combine(site, "Architecture", "includes", "Snippets.html")));
            Assert.DoesNotContain("abbreviations.html", File.ReadAllText(Path.Combine(site, "index.html")));
            Assert.DoesNotContain("Architecture Decision Record", File.ReadAllText(Path.Combine(site, "search-index.json")));

            var imported = File.ReadAllText(Path.Combine(site, "Architecture", "index.html"));
            Assert.Contains("<abbr title=\"Application Programming Interface\">API</abbr>", imported);
            Assert.Contains("<abbr title=\"Configuration Item\">CI</abbr>", imported);
            Assert.Contains("<abbr title=\"Architecture Decision Record\">ADR</abbr>", imported);

            foreach (var sitePage in new[] { Path.Combine(site, "index.html"), Path.Combine(site, "Architecture", "Local.html"), Path.Combine(site, "Backend", "index.html") })
            {
                var html = File.ReadAllText(sitePage);
                Assert.Contains("<abbr title=\"Application Programming Interface\">API</abbr>", html);
                Assert.Contains("<abbr title=\"Continuous Integration\">CI</abbr>", html);
                Assert.DoesNotContain("Architecture Decision Record", html);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsAMissingImportedGlossary()
    {
        var root = CreateTemporaryDirectory();
        var docs = Path.Combine(root, "docs");
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(Path.Combine(repository, "Docs"));
        const string sourceUrl = "https://github.com/example/repository/tree/main/Docs";
        File.WriteAllText(Path.Combine(docs, "README.md"), $"---\nmdu:\n  import:\n    source: {sourceUrl}\n---\n");
        File.WriteAllText(Path.Combine(repository, "Docs", "README.md"), "# Imported\n");
        File.WriteAllText(Path.Combine(repository, "Docs", "mdu.yml"), "glossary: includes/missing.md\n");

        try
        {
            var resolver = new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "repository")));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateGraphBuilder(resolver).CreateGraph(new VisitorSettings { RootPath = docs }, requireImportedGlossaries: true));
            Assert.Contains($"of imported source '{sourceUrl}'", exception.Message);
            Assert.Contains("missing.md", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsAnImportedGlossaryOutsideTheRepository(bool absolute)
    {
        var fixture = new GlossaryImportFixture();
        var secret = Path.Combine(fixture.Root, "secret.md");
        File.WriteAllText(secret, "*[KEY]: Local secret\n");
        fixture.WriteImportedConfiguration($"glossary: '{(absolute ? secret : "../../secret.md")}'\n");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => fixture.CreateGraph(required: true));
            Assert.Contains("must stay inside the imported repository", exception.Message);
            Assert.Contains($"of imported source '{GlossaryImportFixture.SourceUrl}'", exception.Message);

            var site = Path.Combine(fixture.Root, "site");
            var command = new RenderCommand(fixture.Builder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            Assert.Equal(1, command.Execute(new RenderCommandSettings { RootPath = fixture.Docs, OutputPath = site }));
            Assert.False(File.Exists(Path.Combine(site, "index.html")));

            Assert.Null(Assert.Single(fixture.CreateGraph(required: false)).Glossary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void RejectsAnImportedGlossaryWithoutDefinitionsBeforeWritingTheSite()
    {
        var fixture = new GlossaryImportFixture();
        fixture.WriteImportedConfiguration("glossary: includes/abbreviations.md\n");
        var glossary = fixture.WriteRepositoryFile("Docs/includes/abbreviations.md", "No definitions.\n");
        var site = Path.Combine(fixture.Root, "site");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => fixture.CreateGraph(required: true));
            Assert.Contains($"'{glossary}'", exception.Message);
            Assert.Contains($"of imported source '{GlossaryImportFixture.SourceUrl}'", exception.Message);
            Assert.Contains("does not contain any abbreviation definitions", exception.Message);

            var command = new RenderCommand(fixture.Builder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());
            Assert.Equal(1, command.Execute(new RenderCommandSettings { RootPath = fixture.Docs, OutputPath = site }));
            Assert.False(File.Exists(Path.Combine(site, "index.html")));
            Assert.False(File.Exists(Path.Combine(site, "documentation.pdf")));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void TreatsAnUnusableImportedGlossaryAsAbsentOutsideRenderAndServe()
    {
        var fixture = new GlossaryImportFixture();
        fixture.WriteImportedConfiguration("glossary: [not, a, path]\n");
        try
        {
            var root = Assert.Single(fixture.CreateGraph(required: false));
            Assert.Null(root.Glossary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void ExcludesAnExistingUnusableImportedGlossaryOutsideRenderAndServe()
    {
        var fixture = new GlossaryImportFixture();
        fixture.WriteImportedConfiguration("glossary: includes/abbreviations.md\n");
        fixture.WriteRepositoryFile("Docs/includes/abbreviations.md", "No definitions.\n");
        fixture.WriteRepositoryFile("Docs/includes/Snippets.md", "# Snippets\n");
        try
        {
            var root = Assert.Single(fixture.CreateGraph(required: false));
            var pages = new[] { root }.Concat(root.Children).Select(page => page.FileInfo.Name).ToArray();

            Assert.Null(root.Glossary);
            Assert.DoesNotContain("abbreviations.md", pages);
            Assert.Contains("Snippets.md", pages);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void AcceptsAnImportedGlossaryOutsideTheDocsFolderAndIgnoresUnrelatedSettings()
    {
        var fixture = new GlossaryImportFixture();
        fixture.WriteImportedConfiguration("glossary: ../shared/abbreviations.md\nicons: ''\n");
        fixture.WriteRepositoryFile("shared/abbreviations.md", "*[ADR]: Architecture Decision Record\n");
        try
        {
            var root = Assert.Single(fixture.CreateGraph(required: true));
            Assert.NotNull(root.Glossary);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class GlossaryImportFixture : IDisposable
    {
        public const string SourceUrl = "https://github.com/example/glossary/tree/main/Docs";

        public GlossaryImportFixture()
        {
            Root = CreateTemporaryDirectory();
            Docs = Path.Combine(Root, "docs");
            Repository = Path.Combine(Root, "repository");
            Directory.CreateDirectory(Docs);
            Directory.CreateDirectory(Path.Combine(Repository, "Docs"));
            File.WriteAllText(Path.Combine(Docs, "README.md"), $"---\nmdu:\n  import:\n    source: {SourceUrl}\n---\n");
            WriteRepositoryFile("Docs/README.md", "# Imported\n");
            Builder = CreateGraphBuilder(new FakeSourceResolver((SourceUrl, CreateResolvedSource(Repository, "glossary"))));
        }

        public string Root { get; }
        public string Docs { get; }
        public string Repository { get; }
        public GraphBuilder Builder { get; }

        public void WriteImportedConfiguration(string yaml) => WriteRepositoryFile("Docs/mdu.yml", yaml);

        public string WriteRepositoryFile(string relativePath, string content)
        {
            var path = Path.Combine(Repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public MarkdownFile[] CreateGraph(bool required) =>
            Builder.CreateGraph(new VisitorSettings { RootPath = Docs }, requireImportedGlossaries: required);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void RequiresASourceForAnImportDeclaration()
    {
        var root = CreateTemporaryDirectory();
        File.WriteAllText(Path.Combine(root, "README.md"), "---\nmdu:\n  import: {}\n---\n");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateGraphBuilder(new FakeSourceResolver()).CreateGraph(new VisitorSettings { RootPath = root }));
            Assert.Contains("mdu.import.source", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsOpenApiSpecificationsOutsideImportedRepository()
    {
        var root = CreateTemporaryDirectory();
        var repository = Path.Combine(root, "repository");
        var docs = Path.Combine(repository, "Docs");
        Directory.CreateDirectory(docs);
        var sourceUrl = "https://github.com/example/repository/tree/main/Docs";
        File.WriteAllText(Path.Combine(root, "README.md"), $"---\nmdu:\n  import:\n    source: {sourceUrl}\n---\n");
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Remote API\n");
        File.WriteAllText(Path.Combine(docs, "Details.md"), "# Details\n\n```openapi\n../../../../outside.yaml\n```\n");

        try
        {
            var resolver = new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "repository")));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateGraphBuilder(resolver).CreateGraph(new VisitorSettings { RootPath = root }));
            Assert.Contains("inside the imported repository", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsPagesThatMapToTheSameSitePath()
    {
        var root = CreateTemporaryDirectory();
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(Path.Combine(repository, "Docs"));
        var sourceUrl = "https://github.com/example/repository/tree/main/Docs";
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n");
        File.WriteAllText(Path.Combine(root, "index.md"), $"---\nmdu:\n  import:\n    source: {sourceUrl}\n---\n");
        File.WriteAllText(Path.Combine(repository, "Docs", "README.md"), "# Imported\n");

        try
        {
            var resolver = new FakeSourceResolver((sourceUrl, CreateResolvedSource(repository, "repository")));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateGraphBuilder(resolver).CreateGraph(new VisitorSettings { RootPath = root }));
            Assert.Contains("More than one Markdown page maps to 'README.md'", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SkipsAssetsThatWouldBeRenderedOutsideTheSiteRoot()
    {
        var root = CreateTemporaryDirectory();
        var repository = Path.Combine(root, "repository");
        var docs = Path.Combine(repository, "Docs");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(docs);
        var sourceAsset = Path.Combine(repository, "outside.png");
        File.WriteAllText(sourceAsset, "asset");
        var pagePath = Path.Combine(docs, "README.md");
        File.WriteAllText(pagePath, "![Outside](../outside.png)\n");
        var page = new MarkdownFile(new FileInfo(pagePath), null)
        {
            SiteRelativePath = "README.md",
            SourceRootPath = repository
        };

        try
        {
            new LocalAssetsCopy(docs, output).Process(page, 0);

            Assert.True(File.Exists(sourceAsset));
            Assert.False(File.Exists(Path.Combine(root, "outside.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesSlashBranchesAndChecksOutSymlinksAsRegularFiles()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = CreateTemporaryDirectory();
        var sourceRepository = Path.Combine(root, "source");
        var docs = Path.Combine(sourceRepository, "Docs");
        Directory.CreateDirectory(docs);
        var secretPath = Path.Combine(root, "secret.txt");
        File.WriteAllText(secretPath, "secret contents");
        File.WriteAllText(Path.Combine(docs, "README.md"), "# Imported\n");
        File.CreateSymbolicLink(Path.Combine(docs, "secret.md"), secretPath);
        RunGit(sourceRepository, ["init", "-b", "feature/docs"]);
        RunGit(sourceRepository, ["config", "user.email", "mdu-tests@example.com"]);
        RunGit(sourceRepository, ["config", "user.name", "MDU tests"]);
        RunGit(sourceRepository, ["add", "."]);
        RunGit(sourceRepository, ["commit", "-m", "test source"]);
        var commit = RunGit(sourceRepository, ["rev-parse", "HEAD"]).Trim();

        try
        {
            var resolver = new GitTreeSourceResolver(
                new LocalRepositoryGitRunner(sourceRepository, commit, "feature/docs"),
                Path.Combine(root, "cache"));
            var resolved = resolver.Resolve("https://github.com/example/repository/tree/feature/docs/Docs");
            var checkedOutSymlink = new FileInfo(Path.Combine(resolved.SourceFolderPath, "secret.md"));

            Assert.Equal("feature/docs", resolved.ReferenceName);
            Assert.Equal("Docs", resolved.SourcePath);
            Assert.Null(checkedOutSymlink.LinkTarget);
            Assert.NotEqual("secret contents", File.ReadAllText(checkedOutSymlink.FullName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://gitlab.com/group/subgroup/project/-/tree/feature/docs/Docs?ref_type=heads", "feature/docs", "https://gitlab.com/group/subgroup/project.git", "https://gitlab.com/group/subgroup/project/-/edit/feature/docs")]
    [InlineData("https://bitbucket.org/workspace/project/src/main/Docs", "main", "https://bitbucket.org/workspace/project.git", "https://bitbucket.org/workspace/project/src/main")]
    [InlineData("https://bitbucket.org/workspace/project/src/branch/main/Docs", "main", "https://bitbucket.org/workspace/project.git", "https://bitbucket.org/workspace/project/src/branch/main")]
    [InlineData("https://gitea.example.net/team/project/src/branch/main/Docs", "main", "https://gitea.example.net/team/project.git", "https://gitea.example.net/team/project/src/branch/main")]
    [InlineData("https://git.example.net/scm/team/project/tree/main/Docs", "main", "https://git.example.net/scm/team/project.git", "https://git.example.net/scm/team/project/edit/main")]
    public void ResolvesCommonGitHostingTreeUrls(
        string sourceUrl,
        string referenceName,
        string repositoryUrl,
        string editPageLinkPrefix)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner(referenceName), Path.Combine(root, "cache"));
            var source = resolver.Resolve(sourceUrl);

            Assert.Equal(repositoryUrl, source.RepositoryUrl);
            Assert.Equal(referenceName, source.ReferenceName);
            Assert.Equal("Docs", source.SourcePath);
            Assert.Equal(editPageLinkPrefix, source.EditPageLinkPrefix);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://gitlab.com/group/project/-/tree/main/Docs?ref_type=tags", "https://gitlab.com/group/project/-/edit/main")]
    [InlineData("https://gitea.example.net/team/project/src/tag/main/Docs", "https://gitea.example.net/team/project/src/tag/main")]
    public void HonorsExplicitTagReferencesWhenBranchHasTheSameName(string sourceUrl, string editPageLinkPrefix)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main", "main"), Path.Combine(root, "cache"));
            var source = resolver.Resolve(sourceUrl);

            Assert.False(source.IsBranch);
            Assert.Equal(editPageLinkPrefix, source.EditPageLinkPrefix);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNotTreatGitLabNamespaceSegmentsAsHostingRoutes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var source = resolver.Resolve("https://gitlab.com/acme/tools/src/docs-repo");

            Assert.Equal("https://gitlab.com/acme/tools/src/docs-repo.git", source.RepositoryUrl);
            Assert.Equal(string.Empty, source.SourcePath);
            Assert.Equal("main", source.ReferenceName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsGitHubBlobFileUrlsWithAGuidanceMessage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve("https://github.com/example/project/blob/main/Docs/README.md"));

            Assert.Contains("not a repository tree", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsFragmentsOnHostedTreeUrls()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve("https://github.com/example/project/tree/main/Docs#other"));

            Assert.Contains("must not contain a fragment", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IgnoresGitHubBrowseQueryParameters()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var source = resolver.Resolve("https://github.com/example/project/tree/main/Docs?tab=readme");

            Assert.Equal("main", source.ReferenceName);
            Assert.Equal("Docs", source.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsWhenARevisionIsActuallyTreatedAsAFolderOnMain()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve("https://github.com/example/project/tree/missing/Docs"));

            Assert.Contains("default 'main' branch", exception.Message);
            Assert.Contains("include a valid revision", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GivesATagSpecificErrorWhenRefTypeIsUsedAtRepositoryRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main", "v1"), Path.Combine(root, "cache"));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve("https://gitlab.com/group/project/-/tree?ref_type=tags"));

            Assert.Contains("must include a tag revision", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultsToMainForRepositoryAndFolderUrlsWithoutARevision()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("main"), Path.Combine(root, "cache"));
            var repositoryRoot = resolver.Resolve("https://git.example.net/team/project.git");
            var folder = resolver.Resolve("https://git.example.net/team/project/tree/Docs");

            Assert.Equal("main", repositoryRoot.ReferenceName);
            Assert.Equal(string.Empty, repositoryRoot.SourcePath);
            Assert.Equal("main", folder.ReferenceName);
            Assert.Equal("Docs", folder.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesGenericGitCloneUrlsWithAnExplicitRevisionAndFolder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var resolver = new GitTreeSourceResolver(new FixtureGitCommandRunner("feature/docs"), Path.Combine(root, "cache"));
            var source = resolver.Resolve("https://git.example.net/team/project.git#feature/docs/Docs");

            Assert.Equal("https://git.example.net/team/project.git", source.RepositoryUrl);
            Assert.Equal("feature/docs", source.ReferenceName);
            Assert.Equal("Docs", source.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GraphBuilder CreateGraphBuilder(IGitTreeSourceResolver resolver) =>
        new(new DataPreparationVisitor(new MarkdownDocumentReader()), resolver);

    private static ResolvedGitTreeSource CreateResolvedSource(string repositoryPath, string repositoryName) =>
        new(
            $"https://github.com/example/{repositoryName}.git",
            $"https://github.com/example/{repositoryName}",
            "main",
            true,
            "Docs",
            repositoryPath,
            Path.Combine(repositoryPath, "Docs"),
            $"https://github.com/example/{repositoryName}/edit/main");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdu-import-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RunGit(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }

    private sealed class FakeSourceResolver(params (string Url, ResolvedGitTreeSource Source)[] sources)
        : IGitTreeSourceResolver
    {
        private readonly Dictionary<string, ResolvedGitTreeSource> _sources =
            sources.ToDictionary(source => source.Url, source => source.Source, StringComparer.Ordinal);

        public int ResolveCount { get; private set; }

        public ResolvedGitTreeSource Resolve(string sourceUrl)
        {
            ResolveCount++;
            return _sources[sourceUrl];
        }
    }

    private sealed class FixtureGitCommandRunner(string branch, string? tag = null) : IGitCommandRunner
    {
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";

        public string Run(string? workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "ls-remote")
                return $"{Commit}\trefs/heads/{branch}\n" +
                       (tag is null ? string.Empty : $"{Commit}\trefs/tags/{tag}\n");

            if (arguments[0] == "clone")
            {
                Directory.CreateDirectory(arguments[^1]);
                return string.Empty;
            }

            if (arguments[0] == "-c")
            {
                var repository = workingDirectory!;
                Directory.CreateDirectory(Path.Combine(repository, "Docs"));
                File.WriteAllText(Path.Combine(repository, "README.md"), "# Repository\n");
                File.WriteAllText(Path.Combine(repository, "Docs", "README.md"), "# Docs\n");
                return string.Empty;
            }

            if (arguments[0] == "rev-parse") return Commit;
            throw new InvalidOperationException($"Unexpected git command: {string.Join(' ', arguments)}");
        }
    }

    private sealed class LocalRepositoryGitRunner(string sourceRepository, string commit, string branch)
        : IGitCommandRunner
    {
        public string Run(string? workingDirectory, IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "ls-remote")
                return $"{commit}\trefs/heads/{branch}\n";

            var actualArguments = arguments.ToArray();
            if (actualArguments[0] == "clone") actualArguments[6] = sourceRepository;
            return RunGit(workingDirectory ?? Environment.CurrentDirectory, actualArguments);
        }
    }
}
