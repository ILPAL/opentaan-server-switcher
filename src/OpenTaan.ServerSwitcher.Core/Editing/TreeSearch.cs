using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Editing;

public enum SearchMatchField
{
    ElementName,
    AttributeKey,
    AttributeValue,
}

/// <summary>A place in the tree where the search text was found.</summary>
public sealed record SearchMatch(ConfigNode Node, ConfigAttribute? Attribute, SearchMatchField Field);

/// <summary>Find in element names, attribute keys and values; replace in attribute values.</summary>
public static class TreeSearch
{
    public static IReadOnlyList<SearchMatch> Find(ConfigNode root, string query, bool matchCase = false)
    {
        var results = new List<SearchMatch>();
        if (string.IsNullOrEmpty(query))
        {
            return results;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Name.Contains(query, comparison))
            {
                results.Add(new SearchMatch(node, null, SearchMatchField.ElementName));
            }

            foreach (var attribute in node.Attributes)
            {
                if (attribute.Key.Contains(query, comparison))
                {
                    results.Add(new SearchMatch(node, attribute, SearchMatchField.AttributeKey));
                }

                if (attribute.Value.Contains(query, comparison))
                {
                    results.Add(new SearchMatch(node, attribute, SearchMatchField.AttributeValue));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// One undoable step replacing <paramref name="find"/> with <paramref name="replacement"/> in attribute values.
    /// With <paramref name="wholeValue"/>, only values equal to <paramref name="find"/> are replaced
    /// (safer for IPs: "1.2.3.4" will not touch "11.2.3.45"). Returns null when nothing matches.
    /// </summary>
    public static CompositeCommand? CreateReplaceAllCommand(ConfigNode root, string find, string replacement, bool matchCase = false, bool wholeValue = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(find);
        ArgumentNullException.ThrowIfNull(replacement);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var commands = new List<IEditCommand>();
        foreach (var node in root.DescendantsAndSelf())
        {
            foreach (var attribute in node.Attributes)
            {
                string newValue;
                if (wholeValue)
                {
                    if (!string.Equals(attribute.Value, find, comparison))
                    {
                        continue;
                    }

                    newValue = replacement;
                }
                else
                {
                    if (!attribute.Value.Contains(find, comparison))
                    {
                        continue;
                    }

                    newValue = attribute.Value.Replace(find, replacement, comparison);
                }

                if (!string.Equals(newValue, attribute.Value, StringComparison.Ordinal))
                {
                    commands.Add(new SetAttributeValueCommand(node, attribute, newValue));
                }
            }
        }

        return commands.Count == 0 ? null : new CompositeCommand($"Replace \"{find}\" ({commands.Count})", commands);
    }
}
