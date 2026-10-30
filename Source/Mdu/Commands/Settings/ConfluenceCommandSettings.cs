namespace Mdu.Commands.Settings;

public sealed class ConfluenceCommandSettings : IVisitorSettings
{
    public string? RootPath { get; init; }
    public string? SearchPattern { get; init; }
    public bool IncludeHidden { get; init; } = true;
    public string? Url { get; init; }
    public string? SpaceKey { get; init; }
    public string? ParentPageId { get; init; }
    public string ExportId { get; init; } = "documentation";
    public bool DryRun { get; init; }
}
