using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Markdown.Renderer;

// The layout uses XML nodes, but Markdown fragments must first be parsed as HTML.
internal static class PdfHtmlDocument
{
    internal static XmlDocument Parse(string html)
    {
        var parser = new HtmlParser();
        var context = parser.ParseDocument(string.Empty).Body!;
        var nodes = parser.ParseFragment(html, context);
        var document = new XmlDocument { PreserveWhitespace = true };
        var root = document.CreateElement("root");
        document.AppendChild(root);
        foreach (var node in nodes) Copy(node, root, document);
        return document;
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
