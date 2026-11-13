using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Editing;

/// <summary>
/// The "switch server" operation: the config has one <c>channel</c> element per channel, each with its
/// own <c>ip</c> and <c>port</c> attributes, so switching server means updating all of them together.
/// </summary>
public static class ServerEndpoints
{
    public const string ChannelElement = "channel";
    public const string IpKey = "ip";
    public const string PortKey = "port";

    /// <summary>All <c>channel</c> elements that have an <c>ip</c> attribute, in document order.</summary>
    /// <remarks>Element and attribute names are matched ignoring case, like the validator does.</remarks>
    public static IReadOnlyList<ConfigNode> FindChannels(ConfigNode root) =>
        root.DescendantsAndSelf()
            .Where(n => string.Equals(n.Name, ChannelElement, StringComparison.OrdinalIgnoreCase) && FindKey(n, IpKey) is not null)
            .ToList();

    /// <summary>Distinct "ip:port" endpoints currently configured, in first-seen order.</summary>
    public static IReadOnlyList<string> DistinctEndpoints(ConfigNode root) =>
        FindChannels(root)
            .Select(n => GetPort(n) is { } port ? $"{GetIp(n)}:{port}" : GetIp(n)!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Builds one undoable step that sets <c>ip</c> (and <c>port</c> when given) on every channel.
    /// Returns null when nothing would change.
    /// </summary>
    public static CompositeCommand? CreateSwitchCommand(ConfigNode root, string ip, string? port = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        ip = ip.Trim();
        port = string.IsNullOrWhiteSpace(port) ? null : port.Trim();

        var commands = new List<IEditCommand>();
        foreach (var channel in FindChannels(root))
        {
            var ipAttribute = FindKey(channel, IpKey)!;
            if (!string.Equals(ipAttribute.Value, ip, StringComparison.Ordinal))
            {
                commands.Add(new SetAttributeValueCommand(channel, ipAttribute, ip));
            }

            if (port is null)
            {
                continue;
            }

            var portAttribute = FindKey(channel, PortKey);
            if (portAttribute is null)
            {
                commands.Add(new InsertAttributeCommand(channel, new ConfigAttribute(PortKey, port), channel.IndexOfAttribute(ipAttribute) + 1));
            }
            else if (!string.Equals(portAttribute.Value, port, StringComparison.Ordinal))
            {
                commands.Add(new SetAttributeValueCommand(channel, portAttribute, port));
            }
        }

        if (commands.Count == 0)
        {
            return null;
        }

        var label = port is null ? ip : $"{ip}:{port}";
        return new CompositeCommand($"Switch server to {label}", commands);
    }

    /// <summary>The channel's <c>ip</c> value (key matched ignoring case), or null.</summary>
    public static string? GetIp(ConfigNode channel) => FindKey(channel, IpKey)?.Value;

    /// <summary>The channel's <c>port</c> value (key matched ignoring case), or null.</summary>
    public static string? GetPort(ConfigNode channel) => FindKey(channel, PortKey)?.Value;

    /// <summary>The first attribute whose key equals <paramref name="key"/>, ignoring case.</summary>
    private static ConfigAttribute? FindKey(ConfigNode node, string key) =>
        node.Attributes.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
}
