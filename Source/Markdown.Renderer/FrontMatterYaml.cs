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

    /// <summary>
    /// Returns a setting of several parts in any mix of nested and dotted forms, such as <c>content: { code: { annotate: … } }</c>,
    /// <c>content.code: { annotate: … }</c>, or <c>content.code.annotate: …</c>. Nested forms win over dotted ones.
    /// </summary>
    public static YamlNode? FindPath(YamlMappingNode mapping, IReadOnlyList<string> parts)
    {
        for (var count = 1; count <= parts.Count; count++)
        {
            if (!mapping.Children.TryGetValue(new YamlScalarNode(string.Join('.', parts.Take(count))), out var node)) continue;
            if (count == parts.Count) return node;
            if (node is YamlMappingNode nested && FindPath(nested, parts.Skip(count).ToArray()) is { } found) return found;
        }
        return null;
    }
}
