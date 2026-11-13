namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>The bytes are not a valid (or not a supported) TAAN .0m file.</summary>
public sealed class OmFormatException : Exception
{
    public OmFormatException(string message)
        : base(message)
    {
    }

    public OmFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
