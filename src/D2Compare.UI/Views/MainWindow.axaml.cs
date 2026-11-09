using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D2Compare.Core.Models;
using D2Compare.ViewModels;

namespace D2Compare.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;

        foreach (var grid in new[] { ColumnsGrid, RowsGrid, ValuesGrid })
        {
            grid.PropertyChanged += (_, e) =>
            {
                if (e.Property == DataGrid.ItemsSourceProperty)
                {
                    ResetGridLayout(grid);
                    ApplyCollapsedOnLoad(grid);
                }
            };
            grid.AddHandler(
                PointerPressedEvent,
                OnGroupHeaderPointerPressed,
                RoutingStrategies.Tunnel
            );
        }

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
                return;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyWindowSize(vm.Settings);
        };
    }

    // Runs before show: centers on the screen the window was closed on, or the primary one
    private void ApplyWindowSize(AppSettings s)
    {
        var screen =
            (
                s.ScreenX is { } x && s.ScreenY is { } y
                    ? Screens.ScreenFromPoint(new PixelPoint(x, y))
                    : null
            ) ?? Screens.Primary;

        var width = s.WindowWidth;
        var height = s.WindowHeight;
        if (screen is null)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        else
        {
            var area = screen.WorkingArea;
            width = Math.Min(width, area.Width / screen.Scaling);
            height = Math.Min(height, area.Height / screen.Scaling);
            Position = new PixelPoint(
                area.X + (int)((area.Width - width * screen.Scaling) / 2),
                area.Y + (int)((area.Height - height * screen.Scaling) / 2)
            );
        }
        Width = width;
        Height = height;

        if (s.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
            return;

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ColumnsCollapsed):
                QueueFileGroupsCollapsed(ColumnsGrid, vm.ColumnsCollapsed);
                break;
            case nameof(MainViewModel.RowsCollapsed):
                QueueFileGroupsCollapsed(RowsGrid, vm.RowsCollapsed);
                break;
            case nameof(MainViewModel.ValuesCollapsed):
                QueueFileGroupsCollapsed(ValuesGrid, vm.ValuesCollapsed);
                break;
        }
    }

    // Search results stay expanded
    private void ApplyCollapsedOnLoad(DataGrid grid)
    {
        if (DataContext is not MainViewModel { IsBatchView: true, IsSearchFiltered: false } vm)
            return;

        var collapsed =
            grid == ColumnsGrid ? vm.ColumnsCollapsed
            : grid == RowsGrid ? vm.RowsCollapsed
            : vm.ValuesCollapsed;
        if (collapsed)
            QueueFileGroupsCollapsed(grid, true);
    }

    private int _pendingCollapses;

    // Background priority: runs after layout builds the group slots and after the overlay renders
    private void QueueFileGroupsCollapsed(DataGrid grid, bool collapsed)
    {
        if (DataContext is not MainViewModel { IsBatchView: true } vm)
            return;

        var view = grid.ItemsSource;
        _pendingCollapses++;
        vm.IsCollapsingGroups = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    if (grid.ItemsSource == view)
                        SetFileGroupsCollapsed(grid, collapsed);
                }
                finally
                {
                    if (--_pendingCollapses == 0)
                        vm.IsCollapsingGroups = false;
                }
            },
            DispatcherPriority.Background
        );
    }

    // File groups only, data-row groups stay open
    private static void SetFileGroupsCollapsed(DataGrid grid, bool collapsed)
    {
        if (grid.ItemsSource is not DataGridCollectionView { Groups: { } groups })
            return;

        foreach (var group in groups.OfType<DataGridCollectionViewGroup>())
        {
            if (group.Key is not FileSummary)
                return;
            if (collapsed)
                grid.CollapseRowGroup(group, collapseAllSubgroups: false);
            else
                grid.ExpandRowGroup(group, expandAllSubgroups: false);
        }
    }

    // Tunnel + Handled replaces the header's built-in double-click toggle
    private static void OnGroupHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (
            e.Source is not Visual source
            || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed
            || source.FindAncestorOfType<DataGridRowGroupHeader>(includeSelf: true)
                is not { } header
        )
            return;

        var expander = header
            .GetVisualDescendants()
            .OfType<ToggleButton>()
            .FirstOrDefault(b => b.Name == "PART_ExpanderButton");
        if (expander is null || expander == source || expander.IsVisualAncestorOf(source))
            return;

        expander.IsChecked = expander.IsChecked != true;
        e.Handled = true;
    }

    // Auto widths only grow and scroll offsets survive an ItemsSource swap
    private static void ResetGridLayout(DataGrid grid)
    {
        foreach (var column in grid.Columns)
            if (column.Width.IsAuto)
                column.Width = DataGridLength.Auto;

        if (grid.FindDescendantOfType<ScrollViewer>() is { } scrollViewer)
            scrollViewer.Offset = default;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        var s = vm.Settings;

        var cols = PanelsGrid.ColumnDefinitions;
        cols[0].Width = new GridLength(s.Column0Star, GridUnitType.Star);
        cols[2].Width = new GridLength(s.Column2Star, GridUnitType.Star);
        cols[4].Width = new GridLength(s.Column4Star, GridUnitType.Star);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        var s = vm.Settings;

        s.IsMaximized = WindowState == WindowState.Maximized;

        if (Screens.ScreenFromVisual(this) is { } screen)
        {
            var center = screen.Bounds.Center;
            s.ScreenX = center.X;
            s.ScreenY = center.Y;
        }

        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }

        var cols = PanelsGrid.ColumnDefinitions;
        s.Column0Star = cols[0].Width.Value;
        s.Column2Star = cols[2].Width.Value;
        s.Column4Star = cols[4].Width.Value;

        s.Save();
    }
}
