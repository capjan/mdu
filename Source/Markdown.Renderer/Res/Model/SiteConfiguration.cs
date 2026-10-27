namespace Markdown.Renderer.Res.Model;

public sealed class SiteConfiguration
{
    public string SiteName { get; set; } = "Documentation";
    public string? Glossary { get; set; }
    public string? Icons { get; set; }
    public string? LogoLight { get; set; }
    public string? LogoDark { get; set; }
    public string? FaviconLight { get; set; }
    public string? FaviconDark { get; set; }
    public SitePalette? Light { get; set; }
    public SitePalette? Dark { get; set; }
}

public sealed class SitePalette
{
    public string? Background { get; set; }
    public string? Text { get; set; }
    public string? Primary { get; set; }
}
