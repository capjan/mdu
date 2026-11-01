namespace Mdu.Commands.Settings;

public sealed class RenderCommandSettings : IVisitorSettings
{
    public string? SearchPattern { get; init; }

    public bool IncludeHidden { get; init; }

    public string EditPageRoot { get; init; } = string.Empty;

    public string? RootPath { get; init; }

    public string? OutputPath { get; init; }

    public string? PdfRootPage { get; init; }
}
