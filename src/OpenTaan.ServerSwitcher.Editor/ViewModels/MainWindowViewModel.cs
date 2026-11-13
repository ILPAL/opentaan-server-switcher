using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Interop;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;
using OpenTaan.ServerSwitcher.Editor.Services;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

public enum EditorView
{
    Tree,
    Table,
    Xml,
    Changes,
}

/// <summary>The editor workbench: open document, tree, views, inspector, problems, search and server switching.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IEditorHost
{
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Dictionary<ConfigNode, NodeViewModel> _nodeViewModels = new();
    private ConfigDocument? _document;
    private int _matchIndex = -1;
    private bool _xmlStale = true;

    public MainWindowViewModel(IDialogService dialogs, AppSettings settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        Inspector = new InspectorViewModel(this);
        Table = new TableViewModel(this);
        RefreshRecentFiles();
    }

    // ---- IEditorHost ---------------------------------------------------------------------------

    public ConfigDocument? Document => _document;

    public DocumentState State { get; private set; } = DocumentState.Empty;

    public void Execute(IEditCommand command) => _document?.Execute(command);

    public void Select(ConfigNode node)
    {
        if (!_nodeViewModels.TryGetValue(node, out var vm))
        {
            return;
        }

        foreach (var ancestor in node.Ancestors())
        {
            if (_nodeViewModels.TryGetValue(ancestor, out var a))
            {
                a.IsExpanded = true;
            }
        }

        SelectedNode = vm;
        ScrollToNodeRequested?.Invoke(this, vm);
    }

    // ---- Events for the view -------------------------------------------------------------------

    /// <summary>The view should scroll the tree so this node is visible.</summary>
    public event EventHandler<NodeViewModel>? ScrollToNodeRequested;

    /// <summary>The "Switch server" flyout should close.</summary>
    public event EventHandler? SwitchApplied;

    /// <summary>The search box should take keyboard focus.</summary>
    public event EventHandler? FocusSearchRequested;

    // ---- Child view models and collections -----------------------------------------------------

    public InspectorViewModel Inspector { get; }

    public TableViewModel Table { get; }

    public ObservableCollection<NodeViewModel> RootNodes { get; } = new();

    public ObservableCollection<ChangeItemViewModel> Changes { get; } = new();

    public ObservableCollection<ProblemItemViewModel> Problems { get; } = new();

    public ObservableCollection<FileEntryViewModel> Files { get; } = new();

    public ObservableCollection<FileEntryViewModel> RecentFiles { get; } = new();

    // ---- Observable state ----------------------------------------------------------------------

    [ObservableProperty]
    public partial NodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial EditorView ActiveView { get; set; } = EditorView.Tree;

    [ObservableProperty]
    public partial string XmlText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string FileName { get; set; } = string.Empty;

    public string WindowTitle => FileName.Length == 0 ? "OpenTaan Server Switcher" : $"{FileName} — OpenTaan Server Switcher";

    [ObservableProperty]
    public partial string FolderName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FullPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SeedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FormatText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChannelSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ChangeCount { get; set; }

    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    [ObservableProperty]
    public partial int ProblemCount { get; set; }

    [ObservableProperty]
    public partial string ChangeLabel { get; set; } = "No unsaved changes";

    [ObservableProperty]
    public partial string ProblemLabel { get; set; } = "Valid";

    [ObservableProperty]
    public partial string SaveBlockedLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UndoToolTip { get; set; } = "Undo";

    [ObservableProperty]
    public partial string RedoToolTip { get; set; } = "Redo";

    // Search / replace
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReplaceVisible { get; set; }

    [ObservableProperty]
    public partial string ReplaceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ReplaceWholeValue { get; set; } = true;

    // Switch server
    [ObservableProperty]
    public partial string SwitchIp { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SwitchPort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SwitchError { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> CurrentEndpoints { get; set; } = [];

    // ---- Derived flags (raised from Refresh / property-changed hooks) -----------------------------

    public bool HasDocument => _document is not null;

    public bool HasNoDocument => _document is null;

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public bool IsTreeView => ActiveView == EditorView.Tree;

    public bool IsTableView => ActiveView == EditorView.Table;

    public bool IsXmlView => ActiveView == EditorView.Xml;

    public bool IsChangesView => ActiveView == EditorView.Changes;

    // Pane visibility: like Is*View, but hidden while no document is loaded. The panes share a
    // Panel with the welcome screen and are hit-testable, so a visible empty pane would sit on
    // top of the "Open .0m file…" button and swallow its clicks.

    public bool ShowTreeView => HasDocument && IsTreeView;

    public bool ShowTableView => HasDocument && IsTableView;

    public bool ShowXmlView => HasDocument && IsXmlView;

    public bool ShowChangesView => HasDocument && IsChangesView;

    public bool HasChanges => ChangeCount > 0;

    public bool HasNoChanges => ChangeCount == 0;

    public bool HasProblems => ProblemCount > 0;

    public bool HasNoProblems => ProblemCount == 0;

    public bool CanSave => _document is not null && ErrorCount == 0;

    public bool IsSaveBlocked => _document is not null && ErrorCount > 0;

    public bool HasSwitchError => SwitchError is not null;

    public bool HasSearchText => SearchText.Length > 0;

    partial void OnActiveViewChanged(EditorView value)
    {
        OnPropertyChanged(nameof(IsTreeView));
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsXmlView));
        OnPropertyChanged(nameof(IsChangesView));
        RaisePaneVisibilityChanged();
        UpdateXmlIfVisible();
    }

    private void RaisePaneVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowTreeView));
        OnPropertyChanged(nameof(ShowTableView));
        OnPropertyChanged(nameof(ShowXmlView));
        OnPropertyChanged(nameof(ShowChangesView));
    }

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        _document?.History.BreakMerge();
        Inspector.Show(value?.Node, State);
        Table.Update(value?.Node, State);
    }

    partial void OnChangeCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasNoChanges));
    }

    partial void OnProblemCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasNoProblems));
    }

    partial void OnErrorCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsSaveBlocked));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSwitchErrorChanged(string? value) => OnPropertyChanged(nameof(HasSwitchError));

    partial void OnSearchTextChanged(string value)
    {
        _matchIndex = -1;
        OnPropertyChanged(nameof(HasSearchText));
        Refresh();
    }

    // ---- Opening / closing ---------------------------------------------------------------------

    [RelayCommand]
    private async Task Open()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var path = await _dialogs.PickFileToOpenAsync(CurrentFolder ?? _settings.LastFolder);
        if (path is null)
        {
            return;
        }

        var dialog = new OpenFileViewModel(_dialogs, path);
        if (await _dialogs.ShowOpenFileDialogAsync(dialog) && dialog.Content is { } content)
        {
            Load(dialog.FilePath, content);
        }
    }

    /// <summary>Opens a file directly (sidebar, recent files, command line).</summary>
    public async Task OpenPathAsync(string path)
    {
        if (_document?.FilePath is { } current && string.Equals(current, Path.GetFullPath(path), StringComparison.Ordinal))
        {
            return;
        }

        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        try
        {
            Load(path, OmFile.Load(path));
        }
        catch (Exception ex) when (ex is OmFormatException or IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(path))
            {
                _settings.RemoveRecent(path);
                RefreshRecentFiles();
            }

            await _dialogs.ShowErrorAsync("Could not open file", $"{Path.GetFileName(path)}\n\n{ex.Message}");
        }
    }

    /// <summary>Called by the window before closing. Returns false to keep the window open.</summary>
    public Task<bool> ConfirmCloseAsync() => ConfirmDiscardChangesAsync();

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (_document is null || ChangeCount == 0)
        {
            return true;
        }

        return await _dialogs.AskUnsavedChangesAsync(_document.FileName) switch
        {
            UnsavedChoice.Save => await SaveCoreAsync(null),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    private void Load(string path, OmFileContent content)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }

        _document = new ConfigDocument(content, path);
        _document.Changed += OnDocumentChanged;
        _nodeViewModels.Clear();
        RootNodes.Clear();
        SearchText = string.Empty;
        _settings.AddRecent(_document.FilePath!);
        RefreshRecentFiles();

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(HasNoDocument));
        RaisePaneVisibilityChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsSaveBlocked));
        SaveCommand.NotifyCanExecuteChanged();
        UpdateFileInfo();
        Refresh();

        // Start on the first element that has children (the <server> with its channels).
        var start = _document.Root.Children.FirstOrDefault(c => c.Children.Count > 0) ?? _document.Root;
        Select(start);

        var channels = ServerEndpoints.FindChannels(_document.Root).Count;
        StatusMessage = content.Warnings.Count > 0
            ? $"Opened with {content.Warnings.Count} warning(s): {content.Warnings[0]}"
            : $"Opened {_document.FileName} · checksum valid · {channels} channel(s)";
    }

    // ---- Saving / exporting --------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => SaveCoreAsync(copyPath: null);

    [RelayCommand]
    private async Task SaveAs()
    {
        if (_document is null)
        {
            return;
        }

        var path = await _dialogs.PickFileToSaveAsync("Save a copy as .0m", _document.FileName, "0m", "TAAN config", CurrentFolder);
        if (path is not null)
        {
            await SaveCoreAsync(copyPath: path);
        }
    }

    /// <summary>
    /// Saves the open file, or writes a separate copy when <paramref name="copyPath"/> is given.
    /// A copy never changes which file is open, so later saves still go to the original.
    /// </summary>
    private async Task<bool> SaveCoreAsync(string? copyPath)
    {
        if (_document is null)
        {
            return false;
        }

        if (ErrorCount > 0)
        {
            await _dialogs.ShowErrorAsync("Fix problems first", $"{ErrorCount} problem(s) must be fixed before the file can be re-encrypted. See the Problems panel.");
            return false;
        }

        try
        {
            // Picking the open file itself in "Save a copy as" is a normal save.
            if (copyPath is not null && !string.Equals(Path.GetFullPath(copyPath), _document.FilePath, StringComparison.Ordinal))
            {
                var copy = _document.SaveCopy(copyPath);
                _settings.AddRecent(copy.Path);
                RefreshRecentFiles();
                StatusMessage = $"Saved a copy as {Path.GetFileName(copy.Path)} · still editing {_document.FileName}";
                return true;
            }

            var result = _document.Save();
            _settings.AddRecent(result.Path);
            RefreshRecentFiles();
            UpdateFileInfo();
            StatusMessage = result.BackupPath is null
                ? $"Saved {Path.GetFileName(result.Path)} · re-encrypted with original seed 0x{result.Container.Seed:X8}"
                : $"Saved {Path.GetFileName(result.Path)} · original kept as {Path.GetFileName(result.BackupPath)}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync("Save failed", $"Nothing was written.\n\n{ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private Task ExportXml() => ExportAsync("xml", "XML document", XmlProjection.ToXml);

    [RelayCommand]
    private Task ExportJson() => ExportAsync("json", "JSON", JsonProjection.ToJson);

    private async Task ExportAsync(string extension, string typeName, Func<ConfigNode, string> serialize)
    {
        if (_document is null)
        {
            return;
        }

        var name = Path.ChangeExtension(_document.FileName, extension);
        var path = await _dialogs.PickFileToSaveAsync($"Export plain {extension.ToUpperInvariant()}", name, extension, typeName, CurrentFolder);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, serialize(_document.Root), new UTF8Encoding(false));
            StatusMessage = $"Exported {Path.GetFileName(path)} (unencrypted)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync("Export failed", ex.Message);
        }
    }

    // ---- Editing commands ----------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var description = _document?.History.UndoDescription;
        if (_document?.History.Undo() == true)
        {
            StatusMessage = $"Undid: {description}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var description = _document?.History.RedoDescription;
        if (_document?.History.Redo() == true)
        {
            StatusMessage = $"Redid: {description}";
        }
    }

    [RelayCommand]
    private void DiscardAllChanges()
    {
        if (_document?.DiscardAllChanges() == true)
        {
            StatusMessage = "All changes discarded (Undo brings them back)";
        }
    }

    [RelayCommand]
    private void SelectView(EditorView view) => ActiveView = view;

    /// <summary>Fills the Switch server flyout from the current channels.</summary>
    public void PrepareSwitch()
    {
        if (_document is null)
        {
            return;
        }

        var channels = ServerEndpoints.FindChannels(_document.Root);
        CurrentEndpoints = channels
            .Select(c => $"{c.GetValue("name") ?? c.Segment}  →  {ServerEndpoints.GetIp(c)}:{ServerEndpoints.GetPort(c)}")
            .ToList();
        var ips = channels.Select(ServerEndpoints.GetIp).Distinct().ToList();
        var ports = channels.Select(ServerEndpoints.GetPort).Distinct().ToList();
        SwitchIp = ips.Count == 1 ? ips[0] ?? string.Empty : string.Empty;
        SwitchPort = ports.Count == 1 ? ports[0] ?? string.Empty : string.Empty;
        SwitchError = null;
    }

    [RelayCommand]
    private void ApplySwitch()
    {
        if (_document is null)
        {
            return;
        }

        var ip = SwitchIp.Trim();
        var port = SwitchPort.Trim();
        if (!IpAddressRule.IsIPv4(ip))
        {
            SwitchError = "Enter an IPv4 address, e.g. 203.0.113.10";
            return;
        }

        if (port.Length > 0 && !PortRule.IsPort(port))
        {
            SwitchError = "Port must be a whole number from 1 to 65535";
            return;
        }

        if (ServerEndpoints.FindChannels(_document.Root).Count == 0)
        {
            SwitchError = "This file has no <channel> elements with an ip attribute";
            return;
        }

        SwitchError = null;
        var command = ServerEndpoints.CreateSwitchCommand(_document.Root, ip, port.Length == 0 ? null : port);
        SwitchApplied?.Invoke(this, EventArgs.Empty);
        if (command is null)
        {
            StatusMessage = "All channels already use that address";
            return;
        }

        Execute(command);
        StatusMessage = $"{command.Description} · {command.Count} value(s) changed · Save to re-encrypt";
    }

    // ---- Search --------------------------------------------------------------------------------

    [RelayCommand]
    private void FindNext() => StepMatch(+1);

    [RelayCommand]
    private void FindPrevious() => StepMatch(-1);

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleReplace() => IsReplaceVisible = !IsReplaceVisible;

    [RelayCommand]
    private void ReplaceAll()
    {
        if (_document is null || SearchText.Length == 0)
        {
            return;
        }

        var command = TreeSearch.CreateReplaceAllCommand(_document.Root, SearchText, ReplaceText, matchCase: false, wholeValue: ReplaceWholeValue);
        if (command is null)
        {
            StatusMessage = ReplaceWholeValue ? $"No value is exactly \"{SearchText}\"" : $"No value contains \"{SearchText}\"";
            return;
        }

        Execute(command);
        StatusMessage = $"Replaced {command.Count} value(s)";
    }

    private void StepMatch(int direction)
    {
        if (_document is null || SearchText.Length == 0)
        {
            return;
        }

        var nodes = TreeSearch.Find(_document.Root, SearchText).Select(m => m.Node).Distinct().ToList();
        if (nodes.Count == 0)
        {
            SearchStatus = "No matches";
            return;
        }

        _matchIndex = ((_matchIndex + direction) % nodes.Count + nodes.Count) % nodes.Count;
        Select(nodes[_matchIndex]);
        SearchStatus = $"{_matchIndex + 1} of {nodes.Count}";
    }

    // ---- Refresh -------------------------------------------------------------------------------

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _xmlStale = true;
        Refresh();
    }

    /// <summary>Recomputes changes and problems and updates every view from the model.</summary>
    private void Refresh()
    {
        if (_document is null)
        {
            State = DocumentState.Empty;
            return;
        }

        var matches = SearchText.Length > 0 ? TreeSearch.Find(_document.Root, SearchText).Select(m => m.Node) : [];
        State = new DocumentState(_document.GetChanges(), Validator.Default.Validate(_document.Root), matches);

        SyncTree();

        // Selection may point at a node that was just deleted (e.g. by Undo).
        if (SelectedNode is { } selected && !ReferenceEquals(selected.Node.Root, _document.Root))
        {
            SelectedNode = _nodeViewModels[_document.Root];
        }
        else
        {
            Inspector.Show(SelectedNode?.Node, State);
            Table.Update(SelectedNode?.Node, State);
        }

        Changes.Clear();
        foreach (var change in State.Changes)
        {
            Changes.Add(new ChangeItemViewModel(this, change));
        }

        Problems.Clear();
        foreach (var problem in State.Problems.OrderByDescending(p => p.Severity))
        {
            Problems.Add(new ProblemItemViewModel(this, problem));
        }

        ChangeCount = State.Changes.Count;
        ErrorCount = State.ErrorCount;
        ProblemCount = State.Problems.Count;
        ChangeLabel = ChangeCount switch
        {
            0 => "No unsaved changes",
            1 => "1 unsaved change",
            var n => $"{n} unsaved changes",
        };
        ProblemLabel = ProblemCount == 0 ? "Valid" : ErrorCount > 0 ? $"{ErrorCount} error(s)" : $"{ProblemCount} warning(s)";
        SaveBlockedLabel = ErrorCount == 1 ? "Fix 1 problem to save" : $"Fix {ErrorCount} problems to save";

        var history = _document.History;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoToolTip = history.UndoDescription is { } u ? $"Undo {u}" : "Nothing to undo";
        RedoToolTip = history.RedoDescription is { } r ? $"Redo {r}" : "Nothing to redo";

        var channels = ServerEndpoints.FindChannels(_document.Root);
        var endpoints = ServerEndpoints.DistinctEndpoints(_document.Root);
        ChannelSummary = channels.Count == 0
            ? "No channels"
            : endpoints.Count == 1 ? $"{channels.Count} channels → {endpoints[0]}" : $"{channels.Count} channels · {endpoints.Count} endpoints";

        if (SearchText.Length > 0)
        {
            var count = State.Matches.Count;
            SearchStatus = count == 0 ? "No matches" : _matchIndex >= 0 ? $"{_matchIndex + 1} of {count}" : $"{count} match{(count == 1 ? "" : "es")}";
        }
        else
        {
            SearchStatus = string.Empty;
        }

        foreach (var file in Files)
        {
            file.IsModified = file.IsActive && ChangeCount > 0;
        }

        UpdateXmlIfVisible();
    }

    private bool CanUndo() => _document?.History.CanUndo == true;

    private bool CanRedo() => _document?.History.CanRedo == true;

    private void SyncTree()
    {
        var visited = new HashSet<ConfigNode>();
        var root = GetNodeViewModel(_document!.Root, depth: 0);
        SyncNode(root, 0, visited);
        RootNodes.SyncTo([root]);

        foreach (var stale in _nodeViewModels.Keys.Where(n => !visited.Contains(n)).ToList())
        {
            _nodeViewModels.Remove(stale);
        }
    }

    private void SyncNode(NodeViewModel vm, int depth, HashSet<ConfigNode> visited)
    {
        visited.Add(vm.Node);
        vm.Update(State);
        var children = vm.Node.Children.Select(c => GetNodeViewModel(c, depth + 1)).ToList();
        vm.Children.SyncTo(children);
        foreach (var child in children)
        {
            SyncNode(child, depth + 1, visited);
        }
    }

    private NodeViewModel GetNodeViewModel(ConfigNode node, int depth)
    {
        if (!_nodeViewModels.TryGetValue(node, out var vm))
        {
            // New documents open with the first two levels expanded.
            vm = new NodeViewModel(node) { IsExpanded = depth < 2 && node.Children.Count <= 200 };
            _nodeViewModels[node] = vm;
        }

        return vm;
    }

    private void UpdateXmlIfVisible()
    {
        if (_document is null || ActiveView != EditorView.Xml || !_xmlStale)
        {
            return;
        }

        try
        {
            XmlText = XmlProjection.ToXml(_document.Root);
        }
        catch (InvalidOperationException ex)
        {
            XmlText = $"<!-- Cannot show as XML: {ex.Message} -->";
        }

        _xmlStale = false;
    }

    private string? CurrentFolder => _document?.FilePath is { } p ? Path.GetDirectoryName(p) : null;

    private void UpdateFileInfo()
    {
        if (_document is null)
        {
            return;
        }

        FileName = _document.FileName;
        FullPath = _document.FilePath ?? string.Empty;
        FolderName = Path.GetFileName(CurrentFolder ?? string.Empty);
        var c = _document.Container;
        SeedText = c.Seed is { } seed ? $"0x{seed:X8}" : "none";
        FormatText = $"TAAN .0m · {(c.IsCompressed ? "zlib" : "raw")} + {(c.IsEncrypted ? "XOR keystream" : "no encryption")}";
        RefreshFiles();
    }

    private void RefreshFiles()
    {
        Files.Clear();
        if (CurrentFolder is not { } folder || !Directory.Exists(folder))
        {
            return;
        }

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folder, "*.0m").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            paths = _document?.FilePath is { } p ? [p] : [];
        }

        foreach (var path in paths)
        {
            Files.Add(new FileEntryViewModel(path, OpenPathAsync)
            {
                IsActive = string.Equals(path, _document?.FilePath, StringComparison.Ordinal),
            });
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _settings.ClearRecent();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settings.RecentFiles.Where(File.Exists))
        {
            RecentFiles.Add(new FileEntryViewModel(path, OpenPathAsync));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }
}
