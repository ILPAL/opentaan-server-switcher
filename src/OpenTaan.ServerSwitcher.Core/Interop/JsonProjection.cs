using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Interop;

/// <summary>
/// JSON layout of the config tree:
/// <code>{ "name": "…", "metadata": "…", "attributes": [ { "key": "…", "value": "…" } ], "children": [ … ] }</code>
/// <c>attributes</c> and <c>children</c> are omitted when empty.
/// </summary>
public static class JsonProjection
{
    public static string ToJson(ConfigNode root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            Write(writer, root);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ConfigNode FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return Read(document.RootElement, "$");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid JSON: {ex.Message}", ex);
        }
    }

    private static void Write(Utf8JsonWriter writer, ConfigNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("name", node.Name);
        writer.WriteString("metadata", node.Metadata);
        if (node.Attributes.Count > 0)
        {
            writer.WriteStartArray("attributes");
            foreach (var attribute in node.Attributes)
            {
                writer.WriteStartObject();
                writer.WriteString("key", attribute.Key);
                writer.WriteString("value", attribute.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (node.Children.Count > 0)
        {
            writer.WriteStartArray("children");
            foreach (var child in node.Children)
            {
                Write(writer, child);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static ConfigNode Read(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{path}: expected an object.");
        }

        var node = new ConfigNode(GetString(element, "name", path), GetString(element, "metadata", path, optional: true));

        if (element.TryGetProperty("attributes", out var attributes))
        {
            var i = 0;
            foreach (var attribute in EnumerateArray(attributes, $"{path}.attributes"))
            {
                var at = $"{path}.attributes[{i++}]";
                switch (attribute.ValueKind)
                {
                    case JsonValueKind.Object:
                        node.AddAttribute(GetString(attribute, "key", at), GetString(attribute, "value", at));
                        break;
                    case JsonValueKind.Array when attribute.GetArrayLength() == 2:
                        node.AddAttribute(AsString(attribute[0], at + "[0]"), AsString(attribute[1], at + "[1]"));
                        break;
                    default:
                        throw new FormatException($"{at}: expected {{\"key\": …, \"value\": …}} or [key, value].");
                }
            }
        }

        if (element.TryGetProperty("children", out var children))
        {
            var i = 0;
            foreach (var child in EnumerateArray(children, $"{path}.children"))
            {
                node.AddChild(Read(child, $"{path}.children[{i++}]"));
            }
        }

        return node;
    }

    private static JsonElement.ArrayEnumerator EnumerateArray(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
            : throw new FormatException($"{path}: expected an array.");

    private static string GetString(JsonElement element, string property, string path, bool optional = false)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return optional ? string.Empty : throw new FormatException($"{path}: missing \"{property}\".");
        }

        return AsString(value, $"{path}.{property}");
    }

    private static string AsString(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"{path}: expected a string.");
}
