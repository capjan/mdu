using System.Text;
using StaticCode.Icons;

namespace Markdown.Renderer.Test;

public class RepositoryLinkTest
{
    [Theory]
    [InlineData("https://github.com/example/project/edit/main/docs/index.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.example.com/team/api/edit/feature/docs/docs/index.md", "https://github.example.com/team/api", "team/api", "GitHub repository", "simple-github")]
    [InlineData("https://github.com/example/project/edit/main/docs/-/edit/page.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.com/example/project/edit/main/docs/-/ide/project/group/project/edit/main/-/page.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project/edit/main/docs/index.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project/blob/main/docs/index.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project/tree/feature/docs/docs/index.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project/blob/main/docs/-/edit/page.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://github.dev/example/project/blob/main/docs/-/ide/project/group/project/edit/main/-/page.md", "https://github.com/example/project", "example/project", "GitHub repository", "simple-github")]
    [InlineData("https://gitlab.com/group/project/-/edit/main/docs/index.md", "https://gitlab.com/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.com/group/sub/project/-/edit/main/index.md", "https://gitlab.com/group/sub/project", "group/sub/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/group/project/-/edit/main/index.md", "https://code.example.com/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.example.org/group/sub/edit/-/edit/main/index.md", "https://gitlab.example.org/group/sub/edit", "group/sub/edit", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.example.org/group/project/edit/main/index.md", "https://gitlab.example.org/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://git.example.net/team/api/edit/main/index.md", "https://git.example.net/team/api", "team/api", "Repository", "octicons-repo-16")]
    [InlineData("https://gitlab.com/my%20group/project/-/edit/main/index.md", "https://gitlab.com/my%20group/project", "my group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.com/-/ide/project/group/project/edit/main/-/docs/index.md", "https://gitlab.com/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.com/-/ide/project/group/sub/project/edit/main/-/docs/index.md", "https://gitlab.com/group/sub/project", "group/sub/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit/main/-/docs/index.md", "https://code.example.com/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/-/ide/project/group/sub/project/edit/main/-/docs/-/edit/page.md", "https://code.example.com/group/sub/project", "group/sub/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/-/ide/project/group/edit/project/edit/main/-/docs/index.md", "https://code.example.com/group/edit/project", "group/edit/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit/feature%2Fdocs/-/docs/index.md", "https://code.example.com/group/project", "group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://code.example.com/-/ide/project/my%20group/project/edit/main/-/docs/index.md", "https://code.example.com/my%20group/project", "my group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://example.com/gitlab/-/ide/project/group/project/edit/main/-/docs/index.md", "https://example.com/gitlab/group/project", "gitlab/group/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://example.com/tools/git/-/ide/project/group/sub/project/edit/main/-/docs/index.md", "https://example.com/tools/git/group/sub/project", "tools/git/group/sub/project", "GitLab repository", "simple-gitlab")]
    [InlineData("https://gitlab.example.com/gitlab/-/ide/project/group/project/edit/main/-/docs/index.md", "https://gitlab.example.com/gitlab/group/project", "gitlab/group/project", "GitLab repository", "simple-gitlab")]
    public void RendersRepositoryLinkForEditLink(string editPageLink, string repositoryUrl, string repositoryName, string label, string iconShortcode)
    {
        Assert.True(IconCatalog.TryGetIcon(iconShortcode, out var icon));

        var html = HtmlRenderer.RenderRepositoryLink(editPageLink);

        Assert.Equal(
            $"<a class=\"repository-link\" href=\"{repositoryUrl}\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"{label}: {repositoryName}\" aria-label=\"{label}: {repositoryName}\">{icon.ToSvg()}<span>{repositoryName}</span></a>",
            html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("docs/index.md")]
    [InlineData("ftp://github.com/example/project/edit/main/index.md")]
    [InlineData("https://github.com/example/project/blob/main/index.md")]
    [InlineData("https://github.com/example/project/edit")]
    [InlineData("https://gitlab.com/group/project/-/edit")]
    [InlineData("https://gitlab.com/project/-/edit/main/index.md")]
    [InlineData("https://git.example.net/project/-/edit/main/index.md")]
    [InlineData("https://bitbucket.org/team/api/src/main/index.md")]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit")]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit/main")]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit/-/docs/index.md")]
    [InlineData("https://code.example.com/-/ide/project/project/edit/main/-/docs/index.md")]
    [InlineData("https://example.com/gitlab/-/ide/project/project/edit/main/-/docs/index.md")]
    [InlineData("https://example.com/gitlab/-/ide/project/group/project/edit/main")]
    [InlineData("https://github.dev/example")]
    public void RendersNoRepositoryLinkForOtherLinks(string editPageLink)
    {
        Assert.Equal(string.Empty, HtmlRenderer.RenderRepositoryLink(editPageLink));
    }

    [Theory]
    [InlineData("https://gitlab.com/group/sub/project/-/edit/main/index.md", "https://gitlab.com/group/sub/project")]
    [InlineData("https://code.example.com/-/ide/project/group/sub/project/edit/main/-/docs/index.md", "https://code.example.com/group/sub/project")]
    [InlineData("https://example.com/gitlab/-/ide/project/group/sub/project/edit/main/-/docs/index.md", "https://example.com/gitlab/group/sub/project")]
    [InlineData("https://github.com/example/project/edit/main/docs/index.md", "https://github.com/example/project")]
    [InlineData("https://github.dev/example/project/blob/main/docs/index.md", "https://github.com/example/project")]
    public async Task RendersRepositoryLinkInHeader(string editPageLink, string repositoryUrl)
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Test", editPageLink);

        var html = sb.ToString();
        var header = html[html.IndexOf("<header", StringComparison.Ordinal)..html.IndexOf("</header>", StringComparison.Ordinal)];
        Assert.Contains($"<a class=\"repository-link\" href=\"{repositoryUrl}\"", header);
        Assert.Contains($"class=\"page-edit-link\" href=\"{editPageLink}\"", html);
        Assert.DoesNotContain("%%REPOSITORY_LINK%%", html);
    }

    [Theory]
    [InlineData("https://code.example.com/-/ide/project/group/project/edit/main/-/docs/index.md")]
    [InlineData("https://example.com/gitlab/-/ide/project/group/project/edit/main/-/docs/index.md")]
    [InlineData("https://github.dev/example/project/blob/main/docs/index.md")]
    public async Task PreservesWebIdeLinkInFooter(string editPageLink)
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "Content without a heading", editPageLink);

        Assert.Contains($"<a href=\"{editPageLink}\" target=\"_blank\" rel=\"noopener noreferrer\">Edit this page</a>", sb.ToString());
    }
}
