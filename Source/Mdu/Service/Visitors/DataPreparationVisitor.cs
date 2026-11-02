using System.Globalization;
using Mdu.Service.PageTree;
using Markdown.Document;
using Mdu.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service.Visitors;

public class DataPreparationVisitor: IVisitor<MarkdownFile>
{
    public TraversalAlgorithm Algorithm => TraversalAlgorithm.DepthFirst;

    private readonly IMarkdownDocumentReader _markdownReader;

    public DataPreparationVisitor(IMarkdownDocumentReader markdownReader)
    {
        _markdownReader = markdownReader;
    }

    public bool Process(MarkdownFile entity, int graphDepth)
    {
        var header = _markdownReader.ReadHeader(entity.ReadMarkdown());
        entity.HeadingTitle = header.Title;
        entity.FrontMatter = header.FrontMatter;
        var navigationSettings = ReadFrontMatterSettings(header.FrontMatter);
        entity.FrontMatterTitle = navigationSettings.Title;
        entity.Title = navigationSettings.Title ?? header.Title;
        entity.Description = navigationSettings.Description ?? string.Empty;
        entity.Icon = navigationSettings.Icon ?? string.Empty;
        entity.ImportSource = navigationSettings.ImportSource;
        entity.NavigationTitle = navigationSettings.NavigationTitle ?? string.Empty;
        entity.NavigationOrder = navigationSettings.Order;
        entity.NavigationVisible = navigationSettings.Visible;
        entity.NavigationExpanded = navigationSettings.Expanded;
        entity.NavigationPlacement = navigationSettings.Placement;
        entity.InvalidNavigationPlacement = navigationSettings.InvalidPlacement;
        return true;
    }

    private static FrontMatterSettings ReadFrontMatterSettings(string frontMatter)
    {
        if (string.IsNullOrWhiteSpace(frontMatter)) return new FrontMatterSettings();

        var lines = frontMatter.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join(Environment.NewLine, lines)));

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode metadata)
                return new FrontMatterSettings();

            var title = ReadScalar(metadata, "title");
            var description = ReadScalar(metadata, "description");
            var icon = ReadScalar(metadata, "icon");
            var importSource = ReadImportSource(metadata);
            YamlMappingNode? navigation = null;
            if (metadata.Children.TryGetValue(new YamlScalarNode("navigation"), out var navigationNode))
                navigation = navigationNode as YamlMappingNode;

            var navigationTitle = ReadScalar(metadata, "navigation.title");

            YamlNode? orderNode = null;
            YamlNode? visibleNode = null;
            YamlNode? expandedNode = null;
            YamlNode? placementNode = null;
            if (navigation is not null)
            {
                navigationTitle = ReadScalar(navigation, "title") ?? navigationTitle;
                if (navigation.Children.TryGetValue(new YamlScalarNode("order"), out var nestedOrderNode))
                    orderNode = nestedOrderNode;
                if (navigation.Children.TryGetValue(new YamlScalarNode("visible"), out var nestedVisibleNode))
                    visibleNode = nestedVisibleNode;
                if (navigation.Children.TryGetValue(new YamlScalarNode("expanded"), out var nestedExpandedNode))
                    expandedNode = nestedExpandedNode;
                if (navigation.Children.TryGetValue(new YamlScalarNode("placement"), out var nestedPlacementNode))
                    placementNode = nestedPlacementNode;
            }

            if (orderNode is null && metadata.Children.TryGetValue(new YamlScalarNode("navigation.order"), out var compactOrderNode))
            {
                orderNode = compactOrderNode;
            }
            if (visibleNode is null && metadata.Children.TryGetValue(new YamlScalarNode("navigation.visible"), out var compactVisibleNode))
            {
                visibleNode = compactVisibleNode;
            }
            if (expandedNode is null && metadata.Children.TryGetValue(new YamlScalarNode("navigation.expanded"), out var compactExpandedNode))
            {
                expandedNode = compactExpandedNode;
            }
            if (placementNode is null && metadata.Children.TryGetValue(new YamlScalarNode("navigation.placement"), out var compactPlacementNode))
            {
                placementNode = compactPlacementNode;
            }

            int? order = null;
            if (orderNode is YamlScalarNode orderValue &&
                int.TryParse(orderValue.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrder))
            {
                order = parsedOrder;
            }

            var visible = visibleNode is YamlScalarNode visibleValue &&
                          bool.TryParse(visibleValue.Value, out var parsedVisible)
                ? parsedVisible
                : true;
            bool? expanded = expandedNode is YamlScalarNode expandedValue &&
                             bool.TryParse(expandedValue.Value, out var parsedExpanded)
                ? parsedExpanded
                : null;

            var placementSpecified = placementNode is not null;
            var placementValue = placementNode is YamlScalarNode placementScalar ? placementScalar.Value : null;
            var placement = placementValue?.ToLowerInvariant() switch
            {
                "sidebar" => NavigationPlacement.Sidebar,
                "top" => NavigationPlacement.Top,
                _ => NavigationPlacement.Sidebar
            };
            var invalidPlacement = placementSpecified && placementValue?.ToLowerInvariant() is not ("sidebar" or "top")
                ? placementValue ?? "(must be a string)"
                : null;

            return new FrontMatterSettings(title, description, icon, importSource, navigationTitle, order, visible, expanded,
                placement, invalidPlacement);
        }
        catch (YamlException)
        {
            return new FrontMatterSettings();
        }
    }

    private static string? ReadImportSource(YamlMappingNode metadata)
    {
        if (!metadata.Children.TryGetValue(new YamlScalarNode("mdu"), out var mduNode) || mduNode is not YamlMappingNode mdu ||
            !mdu.Children.TryGetValue(new YamlScalarNode("import"), out var importNode))
            return null;

        if (importNode is not YamlMappingNode import)
            throw new InvalidOperationException("The mdu.import frontmatter field must be a mapping with a source URL.");

        var source = ReadScalar(import, "source");
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("The mdu.import.source frontmatter field is required.");

        return source;
    }

    private static string? ReadScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private sealed record FrontMatterSettings(
        string? Title = null,
        string? Description = null,
        string? Icon = null,
        string? ImportSource = null,
        string? NavigationTitle = null,
        int? Order = null,
        bool Visible = true,
        bool? Expanded = null,
        NavigationPlacement Placement = NavigationPlacement.Sidebar,
        string? InvalidPlacement = null);
}
