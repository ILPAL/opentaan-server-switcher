using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Interop;

/// <summary>
/// Shows a config tree as XML and reads it back, losslessly.
/// </summary>
/// <remarks>
/// Most nodes map directly: element name = node name, XML attributes = node attributes (in order).
/// The .0m format can hold things XML cannot, so a few reserved <c>_om.*</c> markers are used only when needed:
/// <list type="bullet">
/// <item><c>_om.meta="…"</c> — the node's metadata string, when not empty;</item>
/// <item><c>&lt;_om.node _om.name="…"&gt;</c> — a node whose name is not a valid XML name (or is empty);</item>
/// <item><c>&lt;_om.attr key="…" value="…"/&gt;</c> — used for all attributes of a node when its keys repeat
/// or are not valid XML names.</item>
/// </list>
/// </remarks>
public static class XmlProjection
{
    private const string Prefix = "_om.";
    private const string NodeElement = "_om.node";
    private const string NameAttribute = "_om.name";
    private const string MetaAttribute = "_om.meta";
    private const string AttrElement = "_om.attr";

    public static string ToXml(ConfigNode root)
    {
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), ToElement(root));
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = new UTF8Encoding(false),
        };

        using var writer = new Utf8StringWriter();
        try
        {
            using (var xml = XmlWriter.Create(writer, settings))
            {
                document.Save(xml);
            }
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("The tree contains characters that XML 1.0 cannot represent (e.g. control characters).", ex);
        }

        return writer.ToString();
    }

    public static ConfigNode FromXml(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Not well-formed XML: {ex.Message}", ex);
        }

        return FromElement(document.Root ?? throw new FormatException("XML has no root element."));
    }

    private static XElement ToElement(ConfigNode node)
    {
        var plainName = IsPlainName(node.Name);
        var element = new XElement(plainName ? node.Name : NodeElement);
        if (!plainName)
        {
            element.SetAttributeValue(NameAttribute, node.Name);
        }

        if (node.Metadata.Length > 0)
        {
            element.SetAttributeValue(MetaAttribute, node.Metadata);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var attributesFit = node.Attributes.All(a => IsPlainName(a.Key) && keys.Add(a.Key));
        foreach (var attribute in node.Attributes)
        {
            if (attributesFit)
            {
                element.Add(new XAttribute(attribute.Key, attribute.Value));
            }
            else
            {
                element.Add(new XElement(AttrElement, new XAttribute("key", attribute.Key), new XAttribute("value", attribute.Value)));
            }
        }

        foreach (var child in node.Children)
        {
            element.Add(ToElement(child));
        }

        return element;
    }

    private static ConfigNode FromElement(XElement element)
    {
        if (element.Name.Namespace != XNamespace.None)
        {
            throw Error(element, $"XML namespaces are not supported (element <{element.Name}>).");
        }

        var localName = element.Name.LocalName;
        string name;
        if (localName == NodeElement)
        {
            name = element.Attribute(NameAttribute)?.Value ?? throw Error(element, $"<{NodeElement}> needs a {NameAttribute} attribute.");
        }
        else if (localName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw Error(element, $"Unknown reserved element <{localName}>.");
        }
        else
        {
            name = localName;
        }

        var node = new ConfigNode(name, element.Attribute(MetaAttribute)?.Value ?? string.Empty);

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var key = attribute.Name.LocalName;
            if (attribute.Name.Namespace != XNamespace.None)
            {
                throw Error(element, $"Namespaced attribute {attribute.Name} is not supported.");
            }

            if (key is NameAttribute or MetaAttribute)
            {
                continue;
            }

            if (key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw Error(element, $"Unknown reserved attribute {key}.");
            }

            node.AddAttribute(key, attribute.Value);
        }

        foreach (var content in element.Nodes())
        {
            switch (content)
            {
                case XElement child when child.Name == AttrElement:
                    node.AddAttribute(
                        child.Attribute("key")?.Value ?? throw Error(child, $"<{AttrElement}> needs a key attribute."),
                        child.Attribute("value")?.Value ?? string.Empty);
                    break;
                case XElement child:
                    node.AddChild(FromElement(child));
                    break;
                case XText text when string.IsNullOrWhiteSpace(text.Value):
                case XComment:
                    break;
                case XText:
                    throw Error(element, $"Text content inside <{localName}> is not supported; use attributes.");
            }
        }

        return node;
    }

    private static bool IsPlainName(string name)
    {
        if (name.Length == 0 || name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static FormatException Error(XObject at, string message) =>
        at is IXmlLineInfo info && info.HasLineInfo()
            ? new FormatException($"Line {info.LineNumber}: {message}")
            : new FormatException(message);

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
