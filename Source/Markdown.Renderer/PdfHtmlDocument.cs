using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Markdown.Renderer;

// The layout uses XML nodes, but Markdown fragments must first be parsed as HTML.
internal static class PdfHtmlDocument
{
    private static readonly string[] LightImageThemeMarkers = ["#only-light", "#gh-light-mode-only"];
    private static readonly string[] DarkImageThemeMarkers = ["#only-dark", "#gh-dark-mode-only"];
    private static readonly string[] ImageThemeMarkers = [.. LightImageThemeMarkers, .. DarkImageThemeMarkers];

    internal static XmlDocument Parse(string html)
    {
        var parser = new HtmlParser();
        var context = parser.ParseDocument(string.Empty).Body!;
        var nodes = parser.ParseFragment(html, context);
        PrepareImageVariants(nodes);
        var document = new XmlDocument { PreserveWhitespace = true };
        var root = document.CreateElement("root");
        document.AppendChild(root);
        foreach (var node in nodes) Copy(node, root, document);
        return document;
    }

    private static void PrepareImageVariants(IEnumerable<INode> nodes)
    {
        foreach (var node in nodes.ToArray())
        {
            if (node is not IElement element) continue;
            if (element.LocalName == "img" && element.GetAttribute("src") is { } originalSource)
            {
                var paragraph = FindAncestor(element, "p");
                if (TryRemoveImageThemeMarker(originalSource, DarkImageThemeMarkers, out var source))
                {
                    var link = element.ParentElement is { LocalName: "a" } parentLink ? parentLink : null;
                    RemoveImageThemeMarkerFromLink(link);
                    element.Remove();
                    if (link is not null && link.Children.Length == 0 && string.IsNullOrWhiteSpace(link.TextContent))
                        link.Remove();
                    RemoveEmptyParagraph(paragraph);
                    continue;
                }

                if (TryRemoveImageThemeMarker(originalSource, LightImageThemeMarkers, out source))
                {
                    element.SetAttribute("src", source!);
                    RemoveImageThemeMarkerFromLink(element.ParentElement is { LocalName: "a" } link ? link : null);
                }
            }

            PrepareImageVariants(element.ChildNodes);
        }
    }

    private static void RemoveImageThemeMarkerFromLink(IElement? link)
    {
        if (link?.GetAttribute("href") is { } href && TryRemoveImageThemeMarker(href, ImageThemeMarkers, out var source))
            link.SetAttribute("href", source!);
    }

    private static IElement? FindAncestor(IElement element, string localName)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
            if (ancestor.LocalName == localName) return ancestor;
        return null;
    }

    private static void RemoveEmptyParagraph(IElement? paragraph)
    {
        if (paragraph is { Children.Length: 0 } && string.IsNullOrWhiteSpace(paragraph.TextContent))
            paragraph.Remove();
    }

    private static bool TryRemoveImageThemeMarker(string? source, string[] markers, out string? cleanSource)
    {
        foreach (var marker in markers)
        {
            if (source?.EndsWith(marker, StringComparison.Ordinal) != true) continue;
            cleanSource = source[..^marker.Length];
            return true;
        }

        cleanSource = source;
        return false;
    }

    private static void Copy(INode node, XmlNode parent, XmlDocument document)
    {
        if (node is IText text)
        {
            parent.AppendChild(document.CreateTextNode(text.Data));
        }
        else if (node is IElement element && element.LocalName is not ("script" or "style" or "template"))
        {
            var copy = document.CreateElement(XmlConvert.EncodeLocalName(element.LocalName));
            foreach (var attribute in element.Attributes)
            {
                var name = XmlConvert.EncodeLocalName(attribute.Name);
                copy.SetAttribute(name, attribute.Value);
            }
            parent.AppendChild(copy);
            foreach (var child in element.ChildNodes) Copy(child, copy, document);
        }
    }
}
