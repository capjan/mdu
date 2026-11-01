using System.Xml.Linq;

namespace Markdown.Confluence;

internal static class ConfluenceStorage
{
    private static readonly XNamespace Ac = "http://atlassian.com/content";

    public static bool AreEquivalent(string actual, string expected)
    {
        if (actual == expected) return true;
        return XNode.DeepEquals(Parse(actual), Parse(expected));
    }

    private static XElement Parse(string storage)
    {
        var root = XElement.Parse("<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" +
            storage + "</root>", LoadOptions.PreserveWhitespace);
        // Confluence persists generated macro IDs; they do not change the rendered content.
        root.Descendants(Ac + "structured-macro").Attributes(Ac + "macro-id").Remove();
        root.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        return root;
    }
}
