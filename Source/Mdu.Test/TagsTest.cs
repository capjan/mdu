using System.Text.Json;
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

[Collection("Process state")]
public class TagsTest
{
    private const string Vocabulary = """
        # Tags

        Topics that span several areas.

        ## Security

        Certificates and secrets.

        ## Kubernetes

        Clusters and ingress.

        Also: k8s

        ## Incident
        """;

    [Fact]
    public void RendersTagLinksTagPagesAndTheVocabularyAsOverview()
    {
        WithSite(StandardSite(), root =>
        {
            var (result, errors) = Render(root);
            Assert.True(result == 0, errors);
            var site = Path.Combine(root, "site");

            var page = File.ReadAllText(Path.Combine(site, "Operations", "Certificates.html"));
            Assert.Matches("</h1>\\s*<ul class=\"page-tags\" aria-label=\"Tags\">.*" +
                           "<a class=\"page-tag\" href=\"../tags/security.html\">Security</a>.*" +
                           "<a class=\"page-tag\" href=\"../tags/kubernetes.html\">Kubernetes</a>", page);

            var security = File.ReadAllText(Path.Combine(site, "tags", "security.html"));
            Assert.Contains("<h1>Security", security);
            Assert.Contains("Certificates and secrets.", security);
            Assert.Contains("3 pages in 2 areas", security);
            Assert.Contains("href=\"kubernetes.html\">Kubernetes <span class=\"page-tag-count\">2</span>", security);
            // Areas follow the navigation; a page shows its description, or else its path below the root page.
            var development = security.IndexOf("<h2 id=\"development\">Development", StringComparison.Ordinal);
            var operations = security.IndexOf("<h2 id=\"operations\">Operations", StringComparison.Ordinal);
            Assert.True(development > 0 && operations > development);
            Assert.Contains("<a href=\"../Operations/Certificates.html\">Rotate certificates</a><span class=\"tag-page-detail\">Renew TLS certificates.</span>", security);
            Assert.Contains("<a href=\"../Operations/Backups.html\">Encrypt backups</a><span class=\"tag-page-detail\">Operations</span>", security);
            Assert.Contains("href=\"../Tags.html\"", security);

            Assert.Contains("url=../Tags.html", File.ReadAllText(Path.Combine(site, "tags", "index.html")));
            var overview = File.ReadAllText(Path.Combine(site, "Tags.html"));
            Assert.Contains("<p class=\"tag-overview-link\"><a href=\"tags/security.html\">3 pages</a></p>", overview);
            Assert.Contains("<a href=\"tags/kubernetes.html\">2 pages</a>", overview);
            // A tag that no page uses gets neither a count nor a page.
            Assert.False(File.Exists(Path.Combine(site, "tags", "incident.html")));
            Assert.DoesNotContain("tags/incident.html", overview);
        });
    }

    [Fact]
    public void AddsTagEntriesAndPageTagsToTheSearchIndex()
    {
        WithSite(StandardSite(), root =>
        {
            Assert.Equal(0, Render(root).Result);
            using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site", "search-index.json")));
            var entries = index.RootElement.EnumerateArray().ToArray();

            var kubernetes = entries.Single(entry => entry.GetProperty("url").GetString() == "tags/kubernetes.html");
            Assert.Equal("tag", kubernetes.GetProperty("kind").GetString());
            Assert.Equal(2, kubernetes.GetProperty("count").GetInt32());
            Assert.Equal("k8s", kubernetes.GetProperty("code").GetString());
            Assert.Equal("Clusters and ingress.", kubernetes.GetProperty("text").GetString());

            var certificates = entries.First(entry => entry.GetProperty("url").GetString()!.StartsWith("Operations/Certificates.html", StringComparison.Ordinal));
            Assert.Equal(["Security", "Kubernetes"], certificates.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
            var home = entries.First(entry => entry.GetProperty("url").GetString()!.StartsWith("index.html", StringComparison.Ordinal));
            Assert.False(home.TryGetProperty("tags", out _));
            Assert.False(home.TryGetProperty("kind", out _));
        });
    }

    [Fact]
    public void UnknownTagStopsRenderingWithASuggestion()
    {
        var files = StandardSite();
        files["Reference/Audit.md"] = "---\ntags: Secruity\n---\n# Audit log\n";
        WithSite(files, root =>
        {
            var (result, errors) = Render(root);

            Assert.Equal(1, result);
            Assert.Contains("Error: Reference/Audit.md: Tag 'Secruity' is not defined in Tags.md. Did you mean 'Security'?", errors);
            Assert.False(File.Exists(Path.Combine(root, "site", "index.html")));
        });
    }

    [Fact]
    public void LintReportsUnknownTagsAndSpellingsAtTheirFrontMatterLine()
    {
        var files = StandardSite();
        files["Reference/Audit.md"] = "---\ntitle: Audit\ntags:\n  - Security\n  - Billing\n---\n# Audit log\n";
        files["Development/Secrets.md"] = "---\ntags: [kubernetes, K8s]\n---\n# Manage secrets\n";
        WithSite(files, root =>
        {
            var diagnostics = Lint(root);

            var unknown = Assert.Single(diagnostics, diagnostic => diagnostic.Message.StartsWith(RuleCodes.UnknownTag, StringComparison.Ordinal));
            Assert.EndsWith(Path.Combine("Reference", "Audit.md"), unknown.FilePath);
            Assert.Equal(5, unknown.LineNumber);
            Assert.False(unknown.IsWarning);
            Assert.Contains("Tag 'Billing' is not defined in Tags.md. Add it as a '## Billing' heading there.", unknown.Message);

            var spelling = Assert.Single(diagnostics, diagnostic => diagnostic.Message.StartsWith(RuleCodes.TagSpelling, StringComparison.Ordinal));
            Assert.True(spelling.IsWarning);
            Assert.Equal(2, spelling.LineNumber);
            Assert.Contains("Tag 'kubernetes' differs from 'Kubernetes'", spelling.Message);
        });
    }

    [Fact]
    public void LintReportsTagsThatAreNeitherATagNorAList()
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "---\ntags:\n  name: Security\n---\n# Home\n"
        }, root =>
        {
            var diagnostic = Assert.Single(Lint(root));
            Assert.Equal($"{RuleCodes.UnknownTag}: The 'tags' front matter must be one tag or a list of tags.", diagnostic.Message);
            Assert.Equal(2, diagnostic.LineNumber);
        });
    }

    [Fact]
    public void WithoutVocabularyTheMostUsedSpellingNamesTheTagAndMduWritesTheOverview()
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "# Home\n",
            ["A.md"] = "---\ntags: [Deployment]\n---\n# A\n",
            ["B.md"] = "---\ntags: [Deployment, C#]\n---\n# B\n",
            ["C.md"] = "---\ntags: [deployment]\n---\n# C\n"
        }, root =>
        {
            var (result, errors) = Render(root);

            Assert.Equal(0, result);
            Assert.Contains("Warning: C.md: Tag 'deployment' differs from 'Deployment' (2 pages). Write it as 'Deployment'.", errors);
            var site = Path.Combine(root, "site");
            Assert.Contains("href=\"tags/deployment.html\">Deployment</a>", File.ReadAllText(Path.Combine(site, "C.html")));
            Assert.Contains("3 pages", File.ReadAllText(Path.Combine(site, "tags", "deployment.html")));
            Assert.True(File.Exists(Path.Combine(site, "tags", "csharp.html")));
            var overview = File.ReadAllText(Path.Combine(site, "tags", "index.html"));
            Assert.Contains("<h1>Tags", overview);
            Assert.Contains("<a href=\"csharp.html\">C#</a><span class=\"tag-overview-count\">1 page</span>", overview);
            Assert.Contains("<a href=\"deployment.html\">Deployment</a><span class=\"tag-overview-count\">3 pages</span>", overview);
        });
    }

    [Fact]
    public void DocumentationFolderNamedTagsCollidesOnlyWhenTheSiteUsesTags()
    {
        var files = new Dictionary<string, string>
        {
            ["README.md"] = "# Home\n",
            ["tags/README.md"] = "# Labels\n"
        };
        WithSite(files, root => Assert.Equal(0, Render(root).Result));

        files["README.md"] = "---\ntags: [Start]\n---\n# Home\n";
        WithSite(files, root =>
        {
            var (result, errors) = Render(root);
            Assert.Equal(1, result);
            Assert.Contains("would be written into the 'tags' output folder", errors);
        });
    }

    [Fact]
    public void DashboardKeepsItsTagsForTagPagesButShowsNoTagLinks()
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "---\nlayout: dashboard\ntags: [Start]\n---\n# Start\n\n## Tools\n\n- [GitHub](https://github.com)\n"
        }, root =>
        {
            Assert.Equal(0, Render(root).Result);
            Assert.DoesNotContain("class=\"page-tags\"", File.ReadAllText(Path.Combine(root, "site", "index.html")));
            Assert.Contains("href=\"../index.html\">Start</a>", File.ReadAllText(Path.Combine(root, "site", "tags", "start.html")));
        });
    }

    [Theory]
    [InlineData("content.tags: Missing.md\n", "Configured tag vocabulary does not exist")]
    [InlineData("content.tags: Empty.md\n", "Empty.md: The tag vocabulary does not define any tags")]
    [InlineData("content.tags: [Tags.md]\n", "The 'content.tags' setting in mdu.yml must be a single value.")]
    public void ReportsUnusableVocabularies(string configuration, string message)
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "# Home\n",
            ["Empty.md"] = "# Empty\n",
            ["mdu.yml"] = configuration
        }, root =>
        {
            var (result, errors) = Render(root);
            Assert.Equal(1, result);
            Assert.Contains(message, errors);
        });
    }

    [Fact]
    public void PdfShowsTheTagsOfEachPage()
    {
        WithSite(StandardSite(), root =>
        {
            var tags = SiteTags.Load(root, SiteConfigurationReader.ReadForContent(root), CreateGraph(root));
            var certificates = tags.Tags.SelectMany(tag => tag.Pages).First(page => page.Title == "Rotate certificates");

            Assert.False(tags.HasErrors);
            Assert.Equal(["Security", "Kubernetes"], tags.For(certificates).Select(tag => tag.Name));
            var output = Path.Combine(root, "manual.pdf");
            var originalOut = Console.Out;
            Console.SetOut(TextWriter.Null);
            try
            {
                Assert.Equal(0, new PdfCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())), new PdfRenderer(new HtmlRenderer()))
                    .Execute(new RenderCommandSettings { RootPath = root, OutputPath = output, SearchPattern = "*.md" }));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
            Assert.True(new FileInfo(output).Length > 0);
        });
    }

    [Fact]
    public void PdfOfOnePageIgnoresTagErrorsOfOtherPages()
    {
        var files = StandardSite();
        files["Development/Secrets.md"] = "---\ntags: [Billing]\n---\n# Manage secrets\n";
        WithSite(files, root =>
        {
            var command = new PdfCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())), new PdfRenderer(new HtmlRenderer()));
            var (topic, _) = Capture(() => command.Execute(new RenderCommandSettings
            {
                RootPath = root, OutputPath = Path.Combine(root, "ops.pdf"), SearchPattern = "*.md", PdfRootPage = "Operations/README.md"
            }));
            var (site, errors) = Capture(() => command.Execute(new RenderCommandSettings
            {
                RootPath = root, OutputPath = Path.Combine(root, "all.pdf"), SearchPattern = "*.md"
            }));

            Assert.Equal(0, topic);
            Assert.Equal(1, site);
            Assert.Contains("Development/Secrets.md: Tag 'Billing' is not defined", errors);
        });
    }

    [Fact]
    public void DeclaredAliasSpellingsAreAcceptedAsWritten()
    {
        var files = StandardSite();
        files["Tags.md"] = Vocabulary.Replace("Also: k8s", "Also: k8s, K8S", StringComparison.Ordinal);
        files["Development/Secrets.md"] = "---\ntags: [K8S]\n---\n# Manage secrets\n";
        WithSite(files, root => Assert.Empty(Lint(root)));
    }

    [Fact]
    public void LintIgnoresUnrelatedProblemsOfMduYml()
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "# Home\n",
            ["mdu.yml"] = "glossary: includes/abbreviations.md\n"
        }, root => Assert.Empty(Lint(root)));
    }

    [Fact]
    public void LintPointsToTheLineOfTheExactTag()
    {
        WithSite(new Dictionary<string, string>
        {
            ["mdu.yml"] = "content.tags: Tags.md\n",
            ["Tags.md"] = Vocabulary,
            ["README.md"] = "---\ntags:\n  - Security\n  - Sec\n---\n# Home\n"
        }, root => Assert.Equal(4, Assert.Single(Lint(root)).LineNumber));
    }

    [Fact]
    public void DescriptionLinksWorkOnTagPages()
    {
        var files = StandardSite();
        files["Tags.md"] = Vocabulary.Replace("Certificates and secrets.",
            "Certificates and [secrets](Development/Secrets.md).", StringComparison.Ordinal);
        WithSite(files, root =>
        {
            Assert.Equal(0, Render(root).Result);
            Assert.Contains("<a href=\"../Development/Secrets.html\">secrets</a>",
                File.ReadAllText(Path.Combine(root, "site", "tags", "security.html")));
            Assert.Contains("<a href=\"Development/Secrets.html\">secrets</a>", File.ReadAllText(Path.Combine(root, "site", "Tags.html")));
        });
    }

    [Fact]
    public void PageWhoseRedirectLandsInTheTagFolderCollides()
    {
        WithSite(new Dictionary<string, string>
        {
            ["README.md"] = "---\ntags: [Start]\n---\n# Home\n",
            ["tags.md"] = "# Labels\n"
        }, root =>
        {
            var (result, errors) = Render(root);
            Assert.Equal(1, result);
            Assert.Contains($"'{Path.Combine("tags", "index.html")}' would be written into the 'tags' output folder", errors);
        });
    }

    private static (int Result, string Errors) Capture(Func<int> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var errors = new StringWriter();
        Console.SetOut(TextWriter.Null);
        Console.SetError(errors);
        try
        {
            return (action(), errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static Dictionary<string, string> StandardSite() => new()
    {
        ["mdu.yml"] = "content:\n  tags: Tags.md\n",
        ["README.md"] = "# Home\n",
        ["Tags.md"] = Vocabulary,
        ["Operations/README.md"] = "# Operations\n",
        ["Operations/Certificates.md"] = "---\ndescription: Renew TLS certificates.\ntags: [Security, k8s]\n---\n# Rotate certificates\n\nText.\n",
        ["Operations/Backups.md"] = "---\ntags: Security\n---\n# Encrypt backups\n",
        ["Development/README.md"] = "# Development\n",
        ["Development/Secrets.md"] = "---\ntags: [Security, Kubernetes]\n---\n# Manage secrets\n"
    };

    private static MarkdownFile[] CreateGraph(string root) =>
        new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()))
            .CreateGraph(new VisitorSettings { RootPath = root, SearchPattern = "*.md" });

    private static IReadOnlyList<LintDiagnostic> Lint(string root)
    {
        var receiver = new ConsoleLintErrorReceiver();
        new LinterVisitor(receiver).Prepare(CreateGraph(root), root);
        return receiver.Diagnostics.Where(diagnostic => diagnostic.Message.StartsWith("MDU026", StringComparison.Ordinal) ||
                                                        diagnostic.Message.StartsWith("MDU027", StringComparison.Ordinal)).ToArray();
    }

    private static (int Result, string Errors) Render(string root)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var errors = new StringWriter();
        Console.SetOut(TextWriter.Null);
        Console.SetError(errors);
        try
        {
            var result = new RenderCommand(
                new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder()).Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = Path.Combine(root, "site"),
                SearchPattern = "*.md"
            });
            return (result, errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static void WithSite(IReadOnlyDictionary<string, string> files, Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-tags-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content);
            }
            test(root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
