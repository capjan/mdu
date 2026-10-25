namespace StaticCode.Diagrams.Isometric;

internal enum IsometricShape
{
    Block,
    Tall,
    Large,
    Disc
}

internal enum IsometricEdgeStyle
{
    Solid,
    Dashed,
    Thick
}

/// <summary>A grid cell, counted from the back corner. Columns run to the front right, rows to the front left.</summary>
internal readonly record struct GridCell(int Column, int Row);

internal sealed class IsometricNode
{
    public required string Id { get; init; }
    public required string Label { get; set; }
    public string? Icon { get; set; }
    public string? Tone { get; set; }
    public IsometricShape Shape { get; set; }
    public GridCell? Cell { get; set; }
    public IsometricZone? Zone { get; init; }

    /// <summary>The first phase with a link to this node; nodes without links appear in the first phase.</summary>
    public int Phase { get; set; } = int.MaxValue;

    /// <summary>The position of a node outside zones among the zones and such nodes, in declaration order.</summary>
    public int Order { get; set; }
}

internal sealed class IsometricZone
{
    public required string Id { get; init; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Tone { get; set; }
    public GridCell? Cell { get; set; }
    public int? Columns { get; set; }
    public List<IsometricNode> Nodes { get; } = new();
    public int Phase => Nodes.Count == 0 ? 1 : Nodes.Min(node => node.Phase);

    /// <summary>The position among the zones and the nodes outside zones, in declaration order.</summary>
    public int Order { get; set; }
}

internal sealed record IsometricEdge(string From, string To, IsometricEdgeStyle Style, string? Label, int Phase);

/// <summary>Zones and nodes on an isometric ground plane, with links that appear phase by phase.</summary>
internal sealed class IsometricDiagram
{
    public IsometricDiagram(
        IReadOnlyList<IsometricZone> zones,
        IReadOnlyList<IsometricNode> nodes,
        IReadOnlyList<IsometricEdge> edges,
        IReadOnlyList<string> phases,
        string? title)
    {
        Zones = zones;
        Nodes = nodes;
        Edges = edges;
        Phases = phases;
        Title = title;
    }

    public IReadOnlyList<IsometricZone> Zones { get; }

    /// <summary>All nodes in declaration order, including the nodes inside zones.</summary>
    public IReadOnlyList<IsometricNode> Nodes { get; }

    public IReadOnlyList<IsometricEdge> Edges { get; }

    /// <summary>Phase titles; empty when the diagram has no phases.</summary>
    public IReadOnlyList<string> Phases { get; }

    public string? Title { get; }
}
