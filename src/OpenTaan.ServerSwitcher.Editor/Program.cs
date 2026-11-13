using Avalonia;

namespace OpenTaan.ServerSwitcher.Editor;

internal static class Program
{
    // Avalonia configuration; don't use any Avalonia, third-party APIs or SynchronizationContext-reliant code
    // before AppMain is called: things aren't initialized yet.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
