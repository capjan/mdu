namespace StaticCode.Diagrams.Component;

public enum ElementKind
{
    Component,
    Interface,
    Actor,
    Database,
    Node,
    Cloud,
    Folder,
    Frame,
    Package,
    Rectangle,
    Card,
    Artifact,
    File,
    Queue,
    Storage,
    Note
}

/// <summary>Where a link points, relative to its source: down and up change ranks, left and right stay in one.</summary>
public enum LinkDirection
{
    Down,
    Up,
    Right,
    Left
}

public enum LinkLine
{
    Solid,
    Dotted,
    Bold
}

public enum LinkHead
{
    None,
    Arrow,
    Triangle
}

public sealed class ComponentElement
{
    public ComponentElement(string id, ElementKind kind, string? parent)
    {
        Id = id;
        Label = id;
        Kind = kind;
        Parent = parent;
    }

    public string Id { get; }
    public string Label { get; set; }
    public ElementKind Kind { get; set; }
    public string? Stereotype { get; set; }
    public string? Href { get; set; }

    /// <summary>The id of the container that holds this element, or <c>null</c> at the top level.</summary>
    public string? Parent { get; }

    /// <summary>Declared with braces, so it is drawn around its children.</summary>
    public bool IsContainer { get; set; }
}

/// <param name="Length">Minimum number of ranks for links that go up or down.</param>
/// <param name="Hidden">Only influences the layout; it is not drawn.</param>
public sealed record ComponentLink(
    string From,
    string To,
    string? Label,
    LinkLine Line,
    LinkHead FromHead,
    LinkHead ToHead,
    LinkDirection Direction,
    int Length = 1,
    bool Hidden = false);

public sealed class ComponentDiagram
{
    // Well above what stays readable; they protect builds from generated diagrams that would take minutes and gigabytes.
    public const int MaxElements = 1000;
    public const int MaxLinks = 2000;

    public ComponentDiagram(
        string? title, bool leftToRight, IReadOnlyList<ComponentElement> elements, IReadOnlyList<ComponentLink> links)
    {
        Title = title;
        LeftToRight = leftToRight;
        Elements = elements;
        Links = links;
    }

    public string? Title { get; }

    /// <summary>Set by <c>left to right direction</c>.</summary>
    public bool LeftToRight { get; }

    public IReadOnlyList<ComponentElement> Elements { get; }
    public IReadOnlyList<ComponentLink> Links { get; }

    public bool IsTooLarge => Elements.Count > MaxElements || Links.Count > MaxLinks;
}
