using Avalonia;
using Avalonia.Media;

namespace D2Compare;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(fontManager => fontManager.AddFontCollection(new AppFontCollection()))
            // Bundled so the UI looks the same on Windows and Linux
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:D2Compare#Noto Sans" })
            .LogToTrace();
}
