namespace OpenTaan.ServerSwitcher.Core.Model;

/// <summary>A key/value pair on a <see cref="ConfigNode"/>. Both parts are UTF-16 strings in the file.</summary>
public sealed class ConfigAttribute
{
    public ConfigAttribute(string key, string value)
        : this(ConfigNode.NewId(), key, value)
    {
    }

    internal ConfigAttribute(int id, string key, string value)
    {
        Id = id;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Id { get; }

    public string Key { get; set; }

    public string Value { get; set; }

    public override string ToString() => $"{Key}=\"{Value}\"";
}
