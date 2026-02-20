using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using D2Compare.ViewModels;
using D2Compare.Views;

using Microsoft.Win32;

namespace D2Compare;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            var mainViewModel = new MainViewModel(mainWindow);
            mainWindow.DataContext = mainViewModel;

            // Handle command-line arguments: --source, --target and --file
            if (desktop.Args is { Length: > 0 })
            {
                string? source = null;
                string? target = null;
                string? file = null;

                for (var i = 0; i < desktop.Args.Length; i++)
                {
                    var arg = desktop.Args[i];
                    switch (arg)
                    {
                        case "--source" when i + 1 < desktop.Args.Length:
                            source = desktop.Args[++i];
                            break;
                        case "--target" when i + 1 < desktop.Args.Length:
                            target = desktop.Args[++i];
                            break;
                        case "--file" when i + 1 < desktop.Args.Length:
                            file = desktop.Args[++i];
                            break;
                    }
                }

                mainViewModel.InitializeFromArguments(source, target, file);
            }

            desktop.MainWindow = mainWindow;

            // On app exit (Windows only), store executable path in HKCU\Software\D2Compare
            desktop.Exit += (_, _) => SaveExecutablePathToRegistry();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void SaveExecutablePathToRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var exePath = Environment.ProcessPath
                          ?? Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrWhiteSpace(exePath))
                return;

            const string keyPath = @"Software\D2Compare";
            const string valueName = "ExecutablePath";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue(valueName, exePath, RegistryValueKind.String);
        }
        catch
        {
            // Writing to the registry is a best-effort operation; ignore failures.
        }
    }
}