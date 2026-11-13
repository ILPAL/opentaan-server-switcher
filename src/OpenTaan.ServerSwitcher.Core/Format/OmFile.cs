using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>A fully decoded .0m file.</summary>
/// <param name="Container">Container header values (seed, flags) — reused unchanged when saving.</param>
/// <param name="Tree">The 14-byte tree header, the root node and any trailing bytes.</param>
/// <param name="Plaintext">The decrypted, decompressed bytes exactly as stored.</param>
/// <param name="Warnings">Non-fatal oddities found while reading.</param>
/// <param name="RoundTripsExactly">
/// True when re-serializing the parsed tree reproduces <paramref name="Plaintext"/> byte for byte.
/// When false, saving would change bytes that are not visible in the editor (e.g. invalid UTF-16).
/// </param>
public sealed record OmFileContent(
    OmContainerInfo Container,
    OmTree Tree,
    byte[] Plaintext,
    IReadOnlyList<string> Warnings,
    bool RoundTripsExactly);

/// <summary>Result of <see cref="OmFile.Save"/>.</summary>
/// <param name="Path">The file that was written.</param>
/// <param name="BackupPath">The backup of the previous file, when one was created by this save.</param>
/// <param name="Size">Size of the written file in bytes.</param>
/// <param name="Container">Header values of the written file (same seed and flags, new checksum and size).</param>
public sealed record OmSaveResult(string Path, string? BackupPath, int Size, OmContainerInfo Container);

/// <summary>Reading and writing whole .0m files: container + tree.</summary>
public static class OmFile
{
    public const string BackupSuffix = ".bak";

    public static OmFileContent Load(string path) => Decode(File.ReadAllBytes(path));

    public static OmFileContent Decode(byte[] fileBytes)
    {
        var decoded = OmContainer.Decode(fileBytes);
        var tree = OmTree.Read(decoded.Plaintext);
        var warnings = decoded.Warnings.ToList();

        if (tree.Trailer.Length > 0)
        {
            warnings.Add($"{tree.Trailer.Length} unexplained byte(s) follow the config tree; they are kept as-is.");
        }

        var roundTrips = tree.Write().AsSpan().SequenceEqual(decoded.Plaintext);
        if (!roundTrips)
        {
            warnings.Add("The tree contains text that cannot be re-encoded exactly (e.g. invalid UTF-16). Saving will normalize it.");
        }

        return new OmFileContent(decoded.Info, tree, decoded.Plaintext, warnings, roundTrips);
    }

    /// <summary>Serializes and encrypts a tree using the container settings (seed, flags) of <paramref name="container"/>.</summary>
    public static byte[] Encode(OmContainerInfo container, byte[] treeHeader, ConfigNode root, byte[]? trailer = null) =>
        OmContainer.Encode(OmTree.Write(treeHeader, root, trailer), container);

    /// <summary>
    /// Safely writes a .0m file:
    /// <list type="number">
    /// <item>encodes with the given container settings (the original seed is reused, never regenerated);</item>
    /// <item>decodes the result again and checks it matches <paramref name="root"/> exactly;</item>
    /// <item>when overwriting, keeps the first version of the file as <c>&lt;file&gt;.bak</c> (an existing backup is never replaced);</item>
    /// <item>writes to a temporary file in the same folder and then replaces the target.</item>
    /// </list>
    /// </summary>
    public static OmSaveResult Save(string path, OmContainerInfo container, byte[] treeHeader, ConfigNode root, byte[]? trailer = null, bool createBackup = true)
    {
        var bytes = Encode(container, treeHeader, root, trailer);
        var written = Verify(bytes, container, root);

        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath) ?? ".";
        Directory.CreateDirectory(directory);

        string? backupPath = null;
        if (createBackup && File.Exists(fullPath))
        {
            var candidate = fullPath + BackupSuffix;
            if (!File.Exists(candidate))
            {
                File.Copy(fullPath, candidate);
                backupPath = candidate;
            }
        }

        var tempPath = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return new OmSaveResult(fullPath, backupPath, bytes.Length, written);
    }

    private static OmContainerInfo Verify(byte[] bytes, OmContainerInfo expected, ConfigNode root)
    {
        OmFileContent check;
        try
        {
            check = Decode(bytes);
        }
        catch (OmFormatException ex)
        {
            throw new InvalidOperationException("Self-check failed: the encoded file could not be decoded again. Nothing was written.", ex);
        }

        if (check.Container.Seed != expected.Seed || check.Container.InnerFlags != expected.InnerFlags || check.Container.OuterFlags != expected.OuterFlags)
        {
            throw new InvalidOperationException("Self-check failed: container settings changed while encoding. Nothing was written.");
        }

        if (!check.Tree.Root.StructurallyEquals(root))
        {
            throw new InvalidOperationException("Self-check failed: the encoded file does not decode to the edited tree. Nothing was written.");
        }

        return check.Container;
    }
}
