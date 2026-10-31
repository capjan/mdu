
using Core.Collections.NodeGraph;
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
    public string? EditPageLink { get; set; }
    public string? AliasRelativePath { get; set; }
    public string? SourceRootPath { get; set; }
    public string? ImportMarkerPath { get; set; }
    /// <summary>The glossary of the imported repository this page comes from; null for pages of the site itself.</summary>
    public Glossary? Glossary { get; set; }

    public string ReadMarkdown() => MarkdownContent ?? File.ReadAllText(FileInfo.FullName);

    public string Title { get; set; } = string.Empty;
    public string HeadingTitle { get; set; } = string.Empty;
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
}
