using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenTaan.ServerSwitcher.Editor.Services;
using OpenTaan.ServerSwitcher.Editor.ViewModels;
using OpenTaan.ServerSwitcher.Editor.Views;

namespace OpenTaan.ServerSwitcher.Editor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var settings = AppSettings.Load();
            var viewModel = new MainWindowViewModel(new DialogService(window), settings);
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // A file passed on the command line (or via "Open with…") is opened directly.
            var fileArgument = desktop.Args?.FirstOrDefault(a => !a.StartsWith('-'));
            window.Opened += async (_, _) =>
            {
                if (fileArgument is not null && File.Exists(fileArgument))
                {
                    await viewModel.OpenPathAsync(fileArgument);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
