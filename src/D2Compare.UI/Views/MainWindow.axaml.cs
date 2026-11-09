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
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnViewModelPropertyChanged;
        };
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

    // New results follow the panel's saved toggle; search results always open so matches show
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

    // Collapsing a large panel takes up to ~1.5s, so the loading overlay stays up until it's
    // done. Background priority runs after layout (the grid builds its group slots there) and
    // after a render, so the overlay is painted before the UI thread blocks.
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

    // Top-level groups only: in batch mode those are files, so data-row groups stay open
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

    // Group headers toggle on a single click instead of the grid's double click. Runs on
    // tunnel and marks the event handled, so the header's own double-click toggle never fires.
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
        // A click on the expander arrow already toggles through the button itself
        if (expander is null || expander == source || expander.IsVisualAncestorOf(source))
            return;

        expander.IsChecked = expander.IsChecked != true;
        e.Handled = true;
    }

    // Auto widths only grow and scroll offsets survive an ItemsSource swap, so a new
    // comparison would otherwise inherit the previous one's layout
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

        Width = s.WindowWidth;
        Height = s.WindowHeight;

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
        if (DataContext is not MainViewModel vm)
            return;
        var s = vm.Settings;

        s.IsMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }

        // Save splitter proportions
        var cols = PanelsGrid.ColumnDefinitions;
        s.Column0Star = cols[0].Width.Value;
        s.Column2Star = cols[2].Width.Value;
        s.Column4Star = cols[4].Width.Value;

        s.Save();
    }
}
