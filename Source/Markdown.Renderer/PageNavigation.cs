using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>Controls the documentation navigation shown within a page.</summary>
public sealed record PageNavigation(bool Breadcrumbs, bool Pagination, bool Toc)
{
    public static PageNavigation? ReadFrontMatter(string? frontMatter)
    {
        if (ReadMetadata(frontMatter) is not { } metadata) return null;

        return new PageNavigation(IsVisible("breadcrumbs"), IsVisible("pagination"), IsVisible("toc"));

        bool IsVisible(string key) =>
            FrontMatterYaml.FindSetting(metadata, "page", key) is not YamlScalarNode scalar
            || !bool.TryParse(scalar.Value, out var visible)
            || visible;
    }

    public static IEnumerable<string> FindProblems(string? frontMatter)
    {
        if (ReadMetadata(frontMatter) is not { } metadata) yield break;

        foreach (var key in new[] { "breadcrumbs", "pagination", "toc" })
        {
            var node = FrontMatterYaml.FindSetting(metadata, "page", key);
            if (node is not null && !(node is YamlScalarNode scalar && bool.TryParse(scalar.Value, out _)))
                yield return $"page.{key} must be 'true' or 'false'.";
        }
    }

    private static YamlMappingNode? ReadMetadata(string? frontMatter) =>
        frontMatter is not null && frontMatter.Contains("page", StringComparison.Ordinal)
            ? FrontMatterYaml.ReadMapping(frontMatter)
            : null;
}
