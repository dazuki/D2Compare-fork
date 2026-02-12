using Avalonia;
using Avalonia.Controls;

using D2Compare.ViewModels;

namespace D2Compare.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var s = vm.Settings;

        Width = s.WindowWidth;
        Height = s.WindowHeight;

        if (s.WindowX.HasValue && s.WindowY.HasValue)
        {
            var pos = new PixelPoint((int)s.WindowX.Value, (int)s.WindowY.Value);
            if (IsPositionOnScreen(pos))
                Position = pos;
        }

        if (s.IsMaximized)
            WindowState = WindowState.Maximized;

        // Restore splitter proportions
        var cols = PanelsGrid.ColumnDefinitions;
        cols[0].Width = new GridLength(s.Column0Star, GridUnitType.Star);
        cols[2].Width = new GridLength(s.Column2Star, GridUnitType.Star);
        cols[4].Width = new GridLength(s.Column4Star, GridUnitType.Star);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var s = vm.Settings;

        s.IsMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
            s.WindowX = Position.X;
            s.WindowY = Position.Y;
        }

        // Save splitter proportions
        var cols = PanelsGrid.ColumnDefinitions;
        s.Column0Star = cols[0].Width.Value;
        s.Column2Star = cols[2].Width.Value;
        s.Column4Star = cols[4].Width.Value;

        s.Save();
    }

    private bool IsPositionOnScreen(PixelPoint pos)
    {
        if (Screens?.All is not { } screens) return false;

        foreach (var screen in screens)
        {
            if (screen.Bounds.Contains(pos))
                return true;
        }

        return false;
    }
}