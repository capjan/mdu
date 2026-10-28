using StaticCode.Diagrams.Flowchart;

namespace StaticCode.Diagrams.State;

public enum StateKind
{
    State,
    Start,
    End,
    Choice,
    Fork,
    Join
}

public sealed class StateNode
{
    public StateNode(string id, StateKind kind = StateKind.State)
    {
        Id = id;
        Label = id;
        Kind = kind;
    }

    public string Id { get; }
    public string Label { get; set; }
    public StateKind Kind { get; set; }
    public string? Href { get; set; }
    public StateDiagram? Content { get; set; }
}

/// <summary>A state diagram, or the local states and transitions inside a composite state.</summary>
public sealed class StateDiagram
{
    public FlowDirection Direction { get; set; } = FlowDirection.TopToBottom;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<StateNode> Nodes { get; } = new();
    public List<FlowchartEdge> Edges { get; } = new();
}
