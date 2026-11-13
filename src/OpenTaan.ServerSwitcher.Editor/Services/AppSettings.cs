using System.Text.Json;

namespace OpenTaan.ServerSwitcher.Editor.Services;

/// <summary>Small per-user settings file (recent files, last folder), stored as JSON in the app-data folder.</summary>
public sealed class AppSettings
{
    private const int MaxRecent = 8;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<string> RecentFiles { get; set; } = new();

    public string? LastFolder { get; set; }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTaanServerSwitcher", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken settings file must never stop the editor from starting.
        }

        return new AppSettings();
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent)
        {
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
        }

        LastFolder = Path.GetDirectoryName(path);
        Save();
    }

    public void RemoveRecent(string path)
    {
        if (RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience only.
        }
    }
}
