
using Mdu.Service.PageTree;
using Markdown.Renderer;

namespace Mdu.Model;

public class MarkdownFile: INode<MarkdownFile>
{
    public MarkdownFile(FileInfo fileInfo, MarkdownFile? parent)
    {
        FileInfo = fileInfo;
        Parent = parent;
    }

    public FileInfo FileInfo { get; }
    public MarkdownFile? Parent { get; set; }
    public MarkdownFile[] Children { get; set; } = Array.Empty<MarkdownFile>();

    public string? SiteRelativePath { get; set; }
    public string? MarkdownContent { get; set; }
    public string? ImportSource { get; set; }
    public bool MergeImportedNavigation { get; set; }
    /// <summary>Source-folder-relative navigation path for a merged import; does not change the mounted site URL.</summary>
    public string? ImportedNavigationRelativePath { get; set; }
    public string? EditPageLink { get; set; }
    public string? AliasRelativePath { get; set; }
    public string? SourceRootPath { get; set; }
    public string? ImportMarkerPath { get; set; }
    /// <summary>The glossary of the imported repository this page comes from; null for pages of the site itself.</summary>
    public Glossary? Glossary { get; set; }

    private string? _markdownSnapshot;

    /// <summary>Reads this node's source snapshot. Call InvalidateMarkdown before reusing the node after a source write.</summary>
    public string ReadMarkdown()
    {
        return MarkdownContent ?? (_markdownSnapshot ??= File.ReadAllText(FileInfo.FullName));
    }

    public void InvalidateMarkdown() => _markdownSnapshot = null;

    public string Title { get; set; } = string.Empty;
    public string HeadingTitle { get; set; } = string.Empty;
    public string? FrontMatterTitle { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string FrontMatter { get; set; } = string.Empty;
    public int? NavigationOrder { get; set; }
    public string NavigationTitle { get; set; } = string.Empty;
    public bool NavigationVisible { get; set; } = true;
    public bool? NavigationExpanded { get; set; }
    public NavigationPlacement NavigationPlacement { get; set; }
    public string? InvalidNavigationPlacement { get; set; }
    public bool IsOnlyReadmeInSection { get; set; }
    /// <summary>The tags of the front matter, as written; empty without them.</summary>
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    /// <summary>True when the front matter has a <c>tags</c> value that is neither one tag nor a list of tags.</summary>
    public bool HasInvalidTags { get; set; }
}
