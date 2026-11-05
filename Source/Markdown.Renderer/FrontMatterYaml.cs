using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>Reads a page's front matter block as YAML.</summary>
public static class FrontMatterYaml
{
    /// <summary>Returns the root mapping, or null when the block is empty, invalid, or not a mapping.</summary>
    public static YamlMappingNode? ReadMapping(string? frontMatter)
    {
        if (string.IsNullOrWhiteSpace(frontMatter)) return null;

        var lines = frontMatter.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join('\n', lines)));
            return yaml.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    /// <summary>Returns a setting written as <c>section: { key: value }</c> or as <c>section.key: value</c>; the nested form wins.</summary>
    public static YamlNode? FindSetting(YamlMappingNode metadata, string section, string key)
    {
        if (metadata.Children.TryGetValue(new YamlScalarNode(section), out var sectionNode) && sectionNode is YamlMappingNode nested &&
            nested.Children.TryGetValue(new YamlScalarNode(key), out var nestedNode))
            return nestedNode;
        return metadata.Children.TryGetValue(new YamlScalarNode($"{section}.{key}"), out var dottedNode) ? dottedNode : null;
    }
}
