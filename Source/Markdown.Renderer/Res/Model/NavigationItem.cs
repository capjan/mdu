namespace Markdown.Renderer.Res.Model;

public class NavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Depth { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsCurrentSection { get; set; }
    public bool IsExpandedByDefault { get; set; }
    public bool IsTopNavigationItem { get; set; }
    public bool IsInTopNavigationSection { get; set; }
    public bool IsInCurrentTopSection { get; set; }
    public bool IsAncestorOfCurrentTopSection { get; set; }
    public int TopNavigationDepth { get; set; }
}
