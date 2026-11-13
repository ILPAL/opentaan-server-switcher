using Avalonia.Controls;
using OpenTaan.ServerSwitcher.Editor.ViewModels;

namespace OpenTaan.ServerSwitcher.Editor.Views;

public partial class OpenFileWindow : Window
{
    private OpenFileViewModel? _viewModel;

    public OpenFileWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as OpenFileViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, bool open) => Close(open);
}
