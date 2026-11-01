using System.Xml.Linq;

namespace Markdown.Confluence;

internal static class ConfluenceStorage
{
    private static readonly XNamespace Ac = "http://atlassian.com/content";
    private static readonly XNamespace Ri = "http://atlassian.com/resource/identifier";

    public static bool AreEquivalent(string actual, string expected, string spaceKey)
    {
        if (actual == expected) return true;
        return XNode.DeepEquals(Parse(actual, spaceKey), Parse(expected, spaceKey));
    }

    private static XElement Parse(string storage, string spaceKey)
    {
        var root = XElement.Parse("<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" +
            storage + "</root>", LoadOptions.PreserveWhitespace);
        // Confluence persists generated macro IDs; they do not change the rendered content.
        root.Descendants(Ac + "structured-macro").Attributes(Ac + "macro-id").Remove();
        // An omitted page space is a relative reference to the containing page's space.
        root.Descendants(Ri + "page").Attributes(Ri + "space-key").Where(attribute => attribute.Value == spaceKey).Remove();
        root.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        return root;
    }
}
