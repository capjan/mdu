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
    public void TagFolderKeepsItsCasingNextToTheVocabularyPageTags()
    {
        WithSite(StandardSite(), root =>
        {
            var site = Path.Combine(root, "site");
            // A folder left from an earlier render, as the redirect of Tags.md creates it on a case-insensitive disk.
            Directory.CreateDirectory(Path.Combine(site, "Tags"));
            var (result, errors) = Render(root);
            Assert.True(result == 0, errors);

            Assert.Contains("tags", Directory.EnumerateDirectories(site).Select(Path.GetFileName), StringComparer.Ordinal);
            Assert.True(File.Exists(Path.Combine(site, "tags", "security.html")));
            var (check, checkErrors) = Capture(() => CheckSiteCommand.Execute(new DirectoryInfo(site), []));
            Assert.True(check == 0, checkErrors);
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

    [Fact]
    public void LintWarnsAboutTagsWithOnePageAndUnusedVocabularyEntries()
    {
        var files = StandardSite();
        files["Operations/Backups.md"] = "---\ntags: [Security, Audit]\n---\n# Encrypt backups\n";
        WithSite(files, root =>
        {
            var receiver = new ConsoleLintErrorReceiver();
            new LinterVisitor(receiver).Prepare(CreateGraph(root), root);
            var unused = receiver.Diagnostics.Where(diagnostic => diagnostic.Message.StartsWith(RuleCodes.UnusedTag, StringComparison.Ordinal)).ToArray();

            Assert.All(unused, diagnostic => Assert.True(diagnostic.IsWarning));
            var single = Assert.Single(unused, diagnostic => diagnostic.Message.Contains("'Audit' is only used on this page", StringComparison.Ordinal));
            Assert.EndsWith(Path.Combine("Operations", "Backups.md"), single.FilePath);
            Assert.Equal(2, single.LineNumber);
            var notUsed = Assert.Single(unused, diagnostic => diagnostic.Message.Contains("'Incident' is not used on any page", StringComparison.Ordinal));
            Assert.EndsWith("Tags.md", notUsed.FilePath);
            Assert.Equal(15, notUsed.LineNumber);
        });
    }

    [Fact]
    public void TagsCommandListsTagsByUseWithTheirAreas()
    {
        WithSite(StandardSite(), root =>
        {
            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            int result;
            try
            {
                result = new TagsCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())))
                    .Execute(new VisitorSettings { RootPath = root, SearchPattern = "*.md" });
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.Equal(0, result);
            Assert.Equal(
                "Security    3  Development, Operations\n" +
                "Kubernetes  2  Development, Operations\n" +
                "Incident    0  only in Tags.md\n",
                output.ToString().ReplaceLineEndings("\n"));
        });
    }

    [Fact]
    public void TagsBlockListsTheTaggedPagesAsCards()
    {
        var files = StandardSite();
        files["Hub.md"] = """
            ---
            tags: [Security]
            ---
            # Security hub

            ```tags
            tags: [Security]
            limit: 2
            ```

            ```tags
            tags: [Security, Kubernetes]
            exclude: k8s
            ```

            ```tags
            tags: Kubernetes
            limit: 0
            ```
            """;
        WithSite(files, root =>
        {
            var (result, errors) = Render(root);
            Assert.True(result == 0, errors);
            var hub = File.ReadAllText(Path.Combine(root, "site", "Hub.html"));

            Assert.DoesNotContain("<code", hub);
            var lists = hub.Split("<div class=\"tag-list\">").Skip(1).Select(list => list[..list.IndexOf("</div>", StringComparison.Ordinal)]).ToArray();
            Assert.Equal(2, lists.Length);
            // The hub itself carries the tag but is not listed; the pages follow the navigation.
            Assert.Equal(["Manage secrets", "Encrypt backups"], LinkTexts(lists[0]));
            Assert.Contains("<span class=\"tag-list-detail\">Development</span>", lists[0]);
            Assert.Contains("<p class=\"tag-list-more\"><a href=\"tags/security.html\">All pages tagged Security</a></p>", hub);
            Assert.Contains("No other pages are tagged Security and Kubernetes without k8s yet.", hub);
            Assert.Equal(["Manage secrets", "Rotate certificates"], LinkTexts(lists[1]));
            Assert.Contains("<span class=\"tag-list-detail\">Operations · Renew TLS certificates.</span>", lists[1]);
            Assert.Equal(1, hub.Split("tag-list-more").Length - 1);

            using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site", "search-index.json")));
            Assert.DoesNotContain(index.RootElement.EnumerateArray(), entry =>
                entry.GetProperty("url").GetString()!.StartsWith("Hub.html", StringComparison.Ordinal) &&
                entry.GetProperty("text").GetString()!.Contains("Manage secrets", StringComparison.Ordinal));
        });

        static string[] LinkTexts(string html) =>
            System.Text.RegularExpressions.Regex.Matches(html, "<a href=\"[^\"]+\">([^<]+)</a>").Select(match => match.Groups[1].Value).ToArray();
    }

    [Theory]
    [InlineData("> [!TIP]\n> ```tags\n> tags: X\n> ```\n", "> [!TIP]\n> \n> - a\n> - b\n> \n")]
    [InlineData("- Topic\n\n  ```tags\n  tags: X\n  ```\n", "- Topic\n\n  \n  - a\n  - b\n  \n")]
    [InlineData("```tags title=\"Hub\"\ntags: X\n```\n", "\n- a\n- b\n\n")]
    public void TagsBlocksInContainersKeepTheirPrefixes(string markdown, string expected) =>
        Assert.Equal(expected, TagListBlock.Expand(markdown, _ => "\n- a\n- b\n"));

    [Fact]
    public void TagsBlockInACalloutStaysInTheCallout()
    {
        var files = StandardSite();
        files["Hub.md"] = "# Hub\n\n> [!TIP]\n> Related pages:\n>\n> ```tags\n> tags: Kubernetes\n> ```\n\nAfter.\n";
        WithSite(files, root =>
        {
            Assert.Equal(0, Render(root).Result);
            var hub = File.ReadAllText(Path.Combine(root, "site", "Hub.html"));
            var callout = hub[hub.IndexOf("markdown-alert-tip", StringComparison.Ordinal)..hub.IndexOf("After.", StringComparison.Ordinal)];

            Assert.Contains("<div class=\"tag-list\">", callout);
            Assert.Contains(">Rotate certificates</a>", callout);
            Assert.DoesNotContain("&gt;", callout);
        });
    }

    [Fact]
    public void TagsBlockOnADashboardBecomesTiles()
    {
        var files = StandardSite();
        files["Start.md"] = "---\nlayout: dashboard\n---\n# Start\n\n## Security\n\n```tags\ntags: Security\n```\n";
        files["Development/Secrets.md"] = "---\ndescription: Keep secrets in the cluster.\nicon: material-key\ntags: [Security, Kubernetes]\n---\n# Manage secrets\n";
        WithSite(files, root =>
        {
            Assert.Equal(0, Render(root).Result);
            var dashboard = File.ReadAllText(Path.Combine(root, "site", "Start.html"));

            Assert.Equal(3, dashboard.Split("class=\"dashboard-tile\"").Length - 1);
            Assert.Contains("Keep secrets in the cluster.", dashboard);
            Assert.DoesNotContain("tag-list", dashboard);
        });
    }

    [Theory]
    [InlineData("tags: [Securty]", 6, "MDU026: The tags block uses tag 'Securty', which is not defined in Tags.md. Did you mean 'Security'?")]
    [InlineData("tags: [Security]\nexclude: [Billing]", 6, "MDU026: The tags block uses tag 'Billing', which is not defined in Tags.md.")]
    [InlineData("tag: Security", 6, "MDU026: The tags block does not know the option 'tag'. Use 'tags', 'exclude', and 'limit'.")]
    [InlineData("tags: [Security]\nlimit: few", 6, "MDU026: 'limit' in the tags block must be a whole number, or 0 for no limit.")]
    public void UnusableTagsBlocksAreLintErrorsAndStopRendering(string options, int line, string message)
    {
        var files = StandardSite();
        files["Hub.md"] = $"---\ntitle: Hub\n---\n# Hub\n\n```tags\n{options}\n```\n";
        WithSite(files, root =>
        {
            var diagnostic = Assert.Single(Lint(root));
            Assert.Equal(message, diagnostic.Message);
            Assert.Equal(line, diagnostic.LineNumber);
            Assert.EndsWith("Hub.md", diagnostic.FilePath);

            var (result, errors) = Render(root);
            Assert.Equal(1, result);
            Assert.Contains($"Error: Hub.md: {message["MDU026: ".Length..]}", errors);
        });
    }

    [Fact]
    public void PdfTagListNamesPagesOutsideTheExportToo()
    {
        var files = StandardSite();
        files["Operations/Hub.md"] = "# Ops hub\n\n```tags\ntags: Security\nlimit: 0\n```\n";
        WithSite(files, root =>
        {
            var output = Path.Combine(root, "ops.pdf");
            var (result, _) = Capture(() => new PdfCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                    new PdfRenderer(new HtmlRenderer()))
                .Execute(new RenderCommandSettings { RootPath = root, OutputPath = output, SearchPattern = "*.md", PdfRootPage = "Operations/README.md" }));

            Assert.Equal(0, result);
            var pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output));
            Assert.Contains(Hex("Encrypt"), pdf);
            // Manage secrets is not part of the Operations export, but the list still names it.
            Assert.Contains(Hex("Manage"), pdf);
            Assert.DoesNotContain(Hex("other"), pdf);
        });

        static string Hex(string text) => Convert.ToHexString(System.Text.Encoding.Latin1.GetBytes(text));
    }

    [Theory]
    // Only pages link to the tag page for the rest, so only they show six entries without a limit.
    [InlineData(TagListStyle.Page, null, 6)]
    [InlineData(TagListStyle.Dashboard, null, 8)]
    [InlineData(TagListStyle.Pdf, null, 8)]
    [InlineData(TagListStyle.Dashboard, 3, 3)]
    [InlineData(TagListStyle.Page, 0, 8)]
    public void DefaultLimitOnlyAppliesToPages(TagListStyle style, int? limit, int shown)
    {
        var pages = Enumerable.Range(1, 8)
            .Select(index => new MarkdownFile(new FileInfo($"Page{index}.md"), null) { Title = $"Page {index}" })
            .ToArray();

        var markdown = TagListBlock.Render(pages, new TagListOptions(["Security"], [], limit), style,
            page => page.Title.Replace(' ', '-') + ".html", _ => string.Empty, "tags/security.html");

        Assert.Equal(shown, markdown.Split("\n- ").Length - 1);
        Assert.Equal(style == TagListStyle.Page && shown < 8, markdown.Contains("All pages tagged Security", StringComparison.Ordinal));
    }

    [Fact]
    public void PagesWithoutUrlAreNamedWithoutLink()
    {
        var numbered = new MarkdownFile(new FileInfo("Start.md"), null) { Title = "1. Getting started" };
        Assert.Equal("\n- 1\\. Getting started (not in this PDF)\n", TagListBlock.Render([numbered],
            new TagListOptions(["Security"], [], null), TagListStyle.Pdf, _ => null, _ => string.Empty, null));

        var pages = new[] { "Inside", "Outside" }
            .Select(title => new MarkdownFile(new FileInfo($"{title}.md"), null) { Title = title }).ToArray();

        var markdown = TagListBlock.Render(pages, new TagListOptions(["Security"], [], null), TagListStyle.Pdf,
            page => page.Title == "Inside" ? "Inside.html" : null, _ => string.Empty, null);

        Assert.Equal("\n- [Inside](<Inside.html>)\n- Outside (not in this PDF)\n", markdown);
    }

    [Fact]
    public void SectionPageCardsShowNoAreaOfTheirOwn()
    {
        var files = StandardSite();
        files["Operations/README.md"] = "---\ndescription: Day-to-day operations.\ntags: [Kubernetes]\n---\n# Operations\n";
        files["Hub.md"] = "# Hub\n\n```tags\ntags: Kubernetes\n```\n";
        WithSite(files, root =>
        {
            Assert.Equal(0, Render(root).Result);
            var hub = File.ReadAllText(Path.Combine(root, "site", "Hub.html"));

            Assert.Contains("<span class=\"tag-list-detail\">Day-to-day operations.</span>", hub);
            Assert.Contains("<span class=\"tag-list-detail\">Operations · Renew TLS certificates.</span>", hub);
        });
    }

    [Fact]
    public void RelativeUrlsEscapeSegmentsButNotParentFolders() =>
        Assert.Equal("../Setup/Install%20Guide.html",
            MarkdownLinks.GetRelativeUrl(Path.Combine(Path.GetTempPath(), "docs", "Usage"),
                Path.Combine(Path.GetTempPath(), "docs", "Setup", "Install Guide.html")));

    [Fact]
    public void PdfListsTheTaggedPagesWithLinksIntoTheDocument()
    {
        var files = StandardSite();
        files["Hub.md"] = "# Hub\n\n```tags\ntags: Kubernetes\n```\n";
        WithSite(files, root =>
        {
            var graph = CreateGraph(root);
            var tags = SiteTags.Load(root, SiteConfigurationReader.ReadForContent(root), graph);
            var hub = graph.SelectMany(page => page.Children.Prepend(page)).Single(page => page.Title == "Hub");
            var block = Assert.Single(TagListBlock.Find(hub.ReadMarkdown()));

            var markdown = TagListBlock.Render(tags.Select(block.Options!, hub), block.Options!, TagListStyle.Pdf,
                page => page.SiteRelativePath!.Replace('\\', '/'), _ => "Area", "tags/kubernetes.html");

            Assert.Equal("\n- [Manage secrets](<Development/Secrets.md>)\n- [Rotate certificates](<Operations/Certificates.md>) — Renew TLS certificates.\n",
                markdown);
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
