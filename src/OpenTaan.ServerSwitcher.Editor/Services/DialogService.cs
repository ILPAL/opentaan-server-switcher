using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenTaan.ServerSwitcher.Editor.ViewModels;
using OpenTaan.ServerSwitcher.Editor.Views;

namespace OpenTaan.ServerSwitcher.Editor.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Everything the view models need from the windowing system. Keeps them free of Avalonia types.</summary>
public interface IDialogService
{
    Task<string?> PickFileToOpenAsync(string? startFolder);

    Task<string?> PickFileToSaveAsync(string title, string suggestedName, string extension, string typeName, string? startFolder);

    /// <summary>Shows the "Open encrypted file" dialog. Returns true when the user chose "Decrypt &amp; open".</summary>
    Task<bool> ShowOpenFileDialogAsync(OpenFileViewModel viewModel);

    Task<UnsavedChoice> AskUnsavedChangesAsync(string fileName);

    Task ShowErrorAsync(string title, string message);
}

public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType OmFiles = new("TAAN config (*.0m)") { Patterns = ["*.0m"] };

    public async Task<string?> PickFileToOpenAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open TAAN config",
            AllowMultiple = false,
            FileTypeFilter = [OmFiles, FilePickerFileTypes.All],
            SuggestedStartLocation = await FolderAsync(startFolder),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileToSaveAsync(string title, string suggestedName, string extension, string typeName, string? startFolder)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }],
            SuggestedStartLocation = await FolderAsync(startFolder),
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ShowOpenFileDialogAsync(OpenFileViewModel viewModel)
    {
        var dialog = new OpenFileWindow { DataContext = viewModel };
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<UnsavedChoice> AskUnsavedChangesAsync(string fileName)
    {
        var index = await MessageDialog.ShowAsync(
            owner,
            "Unsaved changes",
            $"Save the changes to {fileName} before continuing? The file is re-encrypted with its original seed.",
            ["Save", "Don't save", "Cancel"],
            primaryIndex: 0,
            cancelIndex: 2);

        return index switch
        {
            0 => UnsavedChoice.Save,
            1 => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    public Task ShowErrorAsync(string title, string message) =>
        MessageDialog.ShowAsync(owner, title, message, ["OK"], primaryIndex: 0, cancelIndex: 0, isError: true);

    private async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(path);
}
