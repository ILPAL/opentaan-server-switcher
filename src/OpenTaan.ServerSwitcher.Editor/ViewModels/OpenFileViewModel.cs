using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Interop;
using OpenTaan.ServerSwitcher.Editor.Services;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>A labelled header field in the Open dialog, e.g. "0x0E  seed  26 82 EF 83  0x83EF8226".</summary>
public sealed record HeaderField(string Offset, string Name, string Hex, string Meaning);

/// <summary>
/// "Open encrypted file" dialog. The TAAN .0m container stores its own seed, so unlike the generic mockup
/// there is no key to enter: the dialog shows the header, test-decrypts and previews the tree.
/// </summary>
public sealed partial class OpenFileViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public OpenFileViewModel(IDialogService dialogs, string path)
    {
        _dialogs = dialogs;
        FilePath = path;
        ReadHeader();
    }

    /// <summary>The decoded file after a successful test; handed to the editor so it is not decoded twice.</summary>
    public OmFileContent? Content { get; private set; }

    /// <summary>Raised with true (open) or false (cancel).</summary>
    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    public partial string FilePath { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<HeaderField> HeaderFields { get; set; } = [];

    [ObservableProperty]
    public partial string FileInfoText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? HeaderError { get; set; }

    [ObservableProperty]
    public partial bool IsTested { get; set; }

    [ObservableProperty]
    public partial bool TestSucceeded { get; set; }

    [ObservableProperty]
    public partial string? TestError { get; set; }

    [ObservableProperty]
    public partial string PreviewXml { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RootSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Warnings { get; set; } = [];

    public bool IsUntested => !IsTested;

    public bool HasHeaderError => HeaderError is not null;

    public bool HasTestError => TestError is not null;

    partial void OnIsTestedChanged(bool value) => OnPropertyChanged(nameof(IsUntested));

    partial void OnHeaderErrorChanged(string? value) => OnPropertyChanged(nameof(HasHeaderError));

    partial void OnTestErrorChanged(string? value) => OnPropertyChanged(nameof(HasTestError));

    [RelayCommand]
    private async Task Browse()
    {
        var path = await _dialogs.PickFileToOpenAsync(Path.GetDirectoryName(FilePath));
        if (path is not null)
        {
            FilePath = path;
            ReadHeader();
        }
    }

    [RelayCommand]
    private void Test()
    {
        try
        {
            var content = OmFile.Load(FilePath);
            Content = content;
            var root = content.Tree.Root;
            var lines = XmlProjection.ToXml(root).Split('\n');
            PreviewXml = string.Join('\n', lines.Take(14)) + (lines.Length > 14 ? "\n  …" : string.Empty);
            var channels = ServerEndpoints.FindChannels(root).Count;
            RootSummary = $"Root <{root.Name}> · {root.DescendantsAndSelf().Count()} elements · {channels} channel(s)";
            Warnings = content.Warnings;
            TestError = null;
            TestSucceeded = true;
        }
        catch (Exception ex) when (ex is OmFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Content = null;
            TestError = ex.Message;
            TestSucceeded = false;
        }

        IsTested = true;
    }

    [RelayCommand]
    private void ResetTest()
    {
        IsTested = false;
        TestSucceeded = false;
        TestError = null;
    }

    [RelayCommand]
    private void Open()
    {
        if (!TestSucceeded)
        {
            Test();
        }

        if (TestSucceeded)
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private void ReadHeader()
    {
        ResetTest();
        Content = null;
        HeaderFields = [];
        HeaderError = null;
        try
        {
            var bytes = File.ReadAllBytes(FilePath);
            FileInfoText = $"{bytes.Length:N0} bytes · modified {File.GetLastWriteTime(FilePath):yyyy-MM-dd HH:mm}";
            HeaderFields = DescribeHeader(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileInfoText = string.Empty;
            HeaderError = ex.Message;
        }
    }

    private static List<HeaderField> DescribeHeader(byte[] b)
    {
        var fields = new List<HeaderField>();
        void Add(int offset, int length, string name, Func<string> meaning)
        {
            if (b.Length >= offset + length)
            {
                fields.Add(new HeaderField($"{offset:X4}", name, Convert.ToHexString(b, offset, length).Chunk(2).Select(c => new string(c)).Aggregate((x, y) => x + " " + y), meaning()));
            }
        }

        uint U32(int o) => BitConverter.ToUInt32(b, o);
        ushort U16(int o) => BitConverter.ToUInt16(b, o);

        Add(0, 2, "magic", () => U16(0) == OmContainer.Magic ? "0x27AA ✓" : $"0x{U16(0):X4} — not a .0m file");
        Add(2, 2, "flags", () => $"0x{U16(2):X4}");
        Add(4, 4, "data size", () => $"{U32(4):N0} bytes");
        Add(8, 1, "signature", () => b[8] == OmContainer.Signature ? "'S' ✓" : $"0x{b[8]:X2} — unsupported");
        if (b.Length < 10)
        {
            return fields;
        }

        var flags = b[9];
        Add(9, 1, "inner flags", () => $"0x{flags:X2} · {((flags & 1) != 0 ? "zlib" : "raw")} · {((flags & 2) != 0 ? "XOR" : "plain")}");
        Add(10, 4, "checksum", () => $"0x{U32(10):X8}");
        var offset = 14;
        if ((flags & OmContainer.FlagEncrypted) != 0)
        {
            var at = offset;
            Add(at, 4, "seed", () => $"0x{U32(at):X8} · kept");
            offset += 4;
        }

        if ((flags & OmContainer.FlagCompressed) != 0)
        {
            var at = offset;
            Add(at, 4, "plain size", () => $"{U32(at):N0} bytes");
        }

        return fields;
    }
}
