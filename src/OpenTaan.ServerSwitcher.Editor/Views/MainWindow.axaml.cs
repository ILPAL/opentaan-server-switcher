using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenTaan.ServerSwitcher.Editor.ViewModels;

namespace OpenTaan.ServerSwitcher.Editor.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so the document's undo/redo wins over a focused TextBox's own undo.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Leaving a field ends the current undo step ("typing 127.0.0.1" is one step, the next field is another).
        AddHandler(LostFocusEvent, (_, _) => _viewModel?.Document?.History.BreakMerge(), RoutingStrategies.Bubble);

        SearchBox.KeyDown += OnSearchKeyDown;
        if (SwitchButton.Flyout is { } flyout)
        {
            flyout.Opened += (_, _) =>
            {
                _viewModel?.PrepareSwitch();
                Dispatcher.UIThread.Post(() => SwitchIpBox.Focus(), DispatcherPriority.Background);
            };
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ScrollToNodeRequested -= OnScrollToNode;
            _viewModel.SwitchApplied -= OnSwitchApplied;
            _viewModel.FocusSearchRequested -= OnFocusSearch;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollToNodeRequested += OnScrollToNode;
            _viewModel.SwitchApplied += OnSwitchApplied;
            _viewModel.FocusSearchRequested += OnFocusSearch;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || _viewModel is null || _viewModel.ChangeCount == 0)
        {
            return;
        }

        e.Cancel = true;
        if (await _viewModel.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // Cmd on macOS, Ctrl elsewhere.
        var command = Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if ((e.KeyModifiers & command) == 0)
        {
            return;
        }

        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var handled = e.Key switch
        {
            Key.Z when shift => Run(_viewModel.RedoCommand),
            Key.Z => Run(_viewModel.UndoCommand),
            Key.Y => Run(_viewModel.RedoCommand),
            Key.S when shift => Run(_viewModel.SaveAsCommand),
            Key.S => Run(_viewModel.SaveCommand),
            Key.O => Run(_viewModel.OpenCommand),
            Key.F => Run(_viewModel.FocusSearchCommand),
            Key.H => Run(_viewModel.ToggleReplaceCommand),
            Key.D1 => Run(_viewModel.SelectViewCommand, EditorView.Tree),
            Key.D2 => Run(_viewModel.SelectViewCommand, EditorView.Table),
            Key.D3 => Run(_viewModel.SelectViewCommand, EditorView.Xml),
            Key.D4 => Run(_viewModel.SelectViewCommand, EditorView.Changes),
            _ => false,
        };

        e.Handled = handled;
    }

    private static bool Run(System.Windows.Input.ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }

        return true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                _viewModel.FindPreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.FindNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.SearchText = string.Empty;
                Tree.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnFocusSearch(object? sender, EventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnSwitchApplied(object? sender, EventArgs e) => SwitchButton.Flyout?.Hide();

    private void OnScrollToNode(object? sender, NodeViewModel node)
    {
        // Containers for newly expanded items are created on the next layout pass.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (Tree.TreeContainerFromItem(node) is Control container)
                {
                    container.BringIntoView();
                }
            },
            DispatcherPriority.Background);
    }
}
