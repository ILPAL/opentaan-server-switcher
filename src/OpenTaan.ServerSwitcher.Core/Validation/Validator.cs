using System.Globalization;
using System.Text.RegularExpressions;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Validation;

public enum ProblemSeverity
{
    /// <summary>Suspicious but saving is allowed.</summary>
    Warning,

    /// <summary>Blocks saving.</summary>
    Error,
}

/// <summary>A validation finding, pointing at a node (and optionally one of its attributes).</summary>
public sealed record ValidationProblem(ProblemSeverity Severity, string Message, ConfigNode Node, ConfigAttribute? Attribute = null)
{
    public string Location => Attribute is null ? Node.Path : $"{Node.Path} · {Attribute.Key}=\"{Attribute.Value}\"";
}

/// <summary>Checks one node; called for every node in the tree.</summary>
public interface IValidationRule
{
    IEnumerable<ValidationProblem> Check(ConfigNode node);
}

public sealed class Validator
{
    private readonly IReadOnlyList<IValidationRule> _rules;

    public Validator(IEnumerable<IValidationRule> rules)
    {
        _rules = rules.ToList();
    }

    /// <summary>Rules for TAAN client configs.</summary>
    public static Validator Default { get; } = new(
    [
        new ElementNameRule(),
        new DuplicateAttributeKeyRule(),
        new IpAddressRule(),
        new PortRule(),
    ]);

    public IReadOnlyList<ValidationProblem> Validate(ConfigNode root) =>
        root.DescendantsAndSelf().SelectMany(n => _rules.SelectMany(r => r.Check(n))).ToList();
}

/// <summary>Element names must not be empty.</summary>
public sealed class ElementNameRule : IValidationRule
{
    public IEnumerable<ValidationProblem> Check(ConfigNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            yield return new ValidationProblem(ProblemSeverity.Error, "Element name is empty", node);
        }

        foreach (var attribute in node.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key))
            {
                yield return new ValidationProblem(ProblemSeverity.Error, "Attribute key is empty", node, attribute);
            }
        }
    }
}

/// <summary>The format allows repeated keys, but which one takes effect is ambiguous.</summary>
public sealed class DuplicateAttributeKeyRule : IValidationRule
{
    public IEnumerable<ValidationProblem> Check(ConfigNode node)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Key.Length > 0 && !seen.Add(attribute.Key))
            {
                yield return new ValidationProblem(ProblemSeverity.Warning, $"Duplicate attribute \"{attribute.Key}\"", node, attribute);
            }
        }
    }
}

/// <summary>Every <c>ip</c> attribute must be a dotted IPv4 address (a host name only produces a warning).</summary>
public sealed partial class IpAddressRule : IValidationRule
{
    public IEnumerable<ValidationProblem> Check(ConfigNode node)
    {
        foreach (var attribute in node.Attributes.Where(a => string.Equals(a.Key, "ip", StringComparison.OrdinalIgnoreCase)))
        {
            var value = attribute.Value;
            if (IsIPv4(value))
            {
                continue;
            }

            if (value.Length > 0 && value.Trim() != value)
            {
                yield return new ValidationProblem(ProblemSeverity.Error, "ip has leading or trailing spaces", node, attribute);
            }
            else if (HostName().IsMatch(value) && !value.All(c => char.IsAsciiDigit(c) || c == '.'))
            {
                yield return new ValidationProblem(ProblemSeverity.Warning, "ip is a host name; use a numeric IPv4 address to be safe", node, attribute);
            }
            else
            {
                yield return new ValidationProblem(ProblemSeverity.Error, "ip must be an IPv4 address like 203.0.113.10", node, attribute);
            }
        }
    }

    /// <summary>Strict dotted-quad check (IPAddress.TryParse also accepts forms like "1" or "0x7f.1").</summary>
    public static bool IsIPv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit) || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            if (int.Parse(part, CultureInfo.InvariantCulture) > 255)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostName();
}

/// <summary>Every <c>port</c> attribute must be a whole number from 1 to 65535.</summary>
public sealed class PortRule : IValidationRule
{
    public IEnumerable<ValidationProblem> Check(ConfigNode node)
    {
        foreach (var attribute in node.Attributes.Where(a => string.Equals(a.Key, "port", StringComparison.OrdinalIgnoreCase)))
        {
            if (!IsPort(attribute.Value))
            {
                yield return new ValidationProblem(ProblemSeverity.Error, "port must be a whole number from 1 to 65535", node, attribute);
            }
        }
    }

    public static bool IsPort(string value) =>
        value.Length is > 0 and <= 5 &&
        value.All(char.IsAsciiDigit) &&
        int.Parse(value, CultureInfo.InvariantCulture) is >= 1 and <= 65535;
}
