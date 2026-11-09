namespace Markdown.Renderer.Test;

public class TagCatalogTest
{
    private const string Vocabulary = """
        # Tags

        Topics that span several areas.

        ## Security

        Certificates, **secrets**, and access rights.

        ## Kubernetes

        Clusters and ingress.

        Also: k8s, K8S

        ### Versions

        Supported releases.

        ## Migration v3
        """;

    [Fact]
    public void ReadsSecondLevelHeadingsAsTagsWithDescriptionsAndAliases()
    {
        var catalog = TagCatalog.Parse(Vocabulary);

        Assert.Equal(["Security", "Kubernetes", "Migration v3"], catalog.Definitions.Select(tag => tag.Name));
        var kubernetes = catalog.Definitions[1];
        Assert.Equal(["k8s", "K8S"], kubernetes.Aliases);
        Assert.Contains("### Versions", kubernetes.DescriptionMarkdown);
        Assert.DoesNotContain("Also:", kubernetes.DescriptionMarkdown);
        Assert.Equal("Certificates, secrets, and access rights.", catalog.Definitions[0].Description);
        Assert.Equal(string.Empty, catalog.Definitions[2].DescriptionMarkdown);
    }

    [Fact]
    public void IgnoresTheFrontMatterOfTheVocabularyPage()
    {
        var catalog = TagCatalog.Parse("---\nnavigation:\n  visible: false\n---\n\n# Tags\n\n## Security\n");

        Assert.Equal(["Security"], catalog.Definitions.Select(tag => tag.Name));
    }

    [Fact]
    public void OnlyASingleLineAliasListCountsAsAliases()
    {
        var catalog = TagCatalog.Parse("""
            ## Incident

            Also: read the on-call guide before paging.

            Also:
            outage, downtime
            """);

        var incident = Assert.Single(catalog.Definitions);
        Assert.Empty(incident.Aliases);
        Assert.Contains("Also: read the on-call guide before paging.", incident.DescriptionMarkdown);
        Assert.Contains("outage, downtime", incident.DescriptionMarkdown);
    }

    [Theory]
    [InlineData("Security", "Security")]
    [InlineData("security", "Security")]
    [InlineData("  KUBERNETES ", "Kubernetes")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("migration   v3", "Migration v3")]
    public void FindsTagsByNameOrAliasIgnoringCaseAndSpacing(string spelling, string expected) =>
        Assert.Equal(expected, TagCatalog.Parse(Vocabulary).Find(spelling)?.Name);

    [Fact]
    public void SuggestsTheClosestTagForATypo()
    {
        var catalog = TagCatalog.Parse(Vocabulary);

        Assert.Null(catalog.Find("Secruity"));
        Assert.Equal("Security", catalog.Suggest("Secruity"));
        Assert.Equal("Kubernetes", catalog.Suggest("k8"));
        Assert.Null(catalog.Suggest("Billing"));
    }

    [Theory]
    [InlineData("# Tags\n\nNo tags yet.\n", "does not define any tags")]
    [InlineData("## Security\n\n## security\n", "'security' for both 'Security' and 'security'")]
    [InlineData("## Kubernetes\n\nAlso: k8s\n\n## Cluster\n\nAlso: K8s\n", "'K8s' for both 'Kubernetes' and 'Cluster'")]
    public void RejectsVocabulariesWithoutTagsOrWithAmbiguousNames(string markdown, string message)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TagCatalog.Parse(markdown));
        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("Security", "security")]
    [InlineData("Migration v3", "migration-v3")]
    [InlineData("Über uns", "uber-uns")]
    [InlineData("C#", "csharp")]
    [InlineData("C++", "cplusplus")]
    [InlineData("--", "tag")]
    public void CreatesFileNamesForTagPages(string name, string slug) => Assert.Equal(slug, TagCatalog.Slug(name));

    [Fact]
    public void InsertsTagLinksAfterTheTitleAndCountsBelowVocabularySections()
    {
        var links = new[] { new PageTagLink("Security", "../tags/security.html", 2), new PageTagLink("Audit & Logs", "../tags/audit-logs.html", 1) };

        var page = PageTags.InsertAfterTitle("<h1 id=\"home\">Home</h1>\n<p>Text</p>\n", links);
        Assert.Matches("</h1>\n<ul class=\"page-tags\" aria-label=\"Tags\">.*href=\"../tags/security.html\">Security</a>.*Audit &amp; Logs</a></li></ul>\n+<p>Text</p>", page);
        Assert.StartsWith("<ul class=\"page-tags\"", PageTags.InsertAfterTitle("<p>No title</p>", links));

        var vocabulary = PageTags.InsertSectionLinks(
            "<h1>Tags</h1>\n<h2 id=\"security\">Security</h2>\n<p>Secrets.</p>\n<h2>Audit &amp; Logs</h2>\n<h2>Other</h2>\n", links);
        Assert.Contains("<p>Secrets.</p>\n<p class=\"tag-overview-link\"><a href=\"../tags/security.html\">2 pages</a></p>\n<h2>Audit", vocabulary);
        Assert.Contains("<h2>Audit &amp; Logs</h2>\n<p class=\"tag-overview-link\"><a href=\"../tags/audit-logs.html\">1 page</a></p>\n<h2>Other</h2>", vocabulary);
    }
}
