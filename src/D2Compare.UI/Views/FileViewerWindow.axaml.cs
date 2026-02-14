using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D2Compare.Views;

public partial class FileViewerWindow : Window
{
    private DataGridTextColumn? _firstDataColumn;
    private List<string[]>? _rows;
    private string _firstColumnHeader = "";
    private int _lastSearchIndex = -1;

    public FileViewerWindow()
    {
        InitializeComponent();
    }

    public FileViewerWindow(string filePath, string label = "") : this()
    {
        Title = string.IsNullOrEmpty(label)
            ? Path.GetFileName(filePath)
            : $"{Path.GetFileName(filePath)} - {label}";
        LoadTsv(filePath);
        FreezeCheckBox.IsCheckedChanged += (_, _) => UpdateFrozenState();
        CopyRowCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (CopyRowCheckBox.IsChecked != true)
                IncludeHeaderCheckBox.IsChecked = false;
        };
        SearchModeToggle.IsCheckedChanged += OnSearchModeChanged;
        Grid.KeyDown += OnGridKeyDown;
        SearchBox.KeyDown += OnSearchKeyDown;
    }

    private void LoadTsv(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return;

        var headers = lines[0].Split('\t');
        _firstColumnHeader = headers[0];

        // Row number column (always frozen)
        var rowNumCol = new DataGridTextColumn
        {
            Header = "(Row)",
            Binding = new Binding("[0]"),
            IsReadOnly = true
        };
        rowNumCol.CellStyleClasses.Add("frozen");
        Grid.Columns.Add(rowNumCol);

        // First data column
        _firstDataColumn = new DataGridTextColumn
        {
            Header = headers[0],
            Binding = new Binding("[1]"),
            IsReadOnly = true
        };
        Grid.Columns.Add(_firstDataColumn);

        // Remaining columns
        for (int i = 1; i < headers.Length; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new Binding($"[{i + 1}]"),
                IsReadOnly = true
            });
        }

        _rows = new List<string[]>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split('\t');
            var row = new string[headers.Length + 1];
            row[0] = i.ToString();
            for (int j = 0; j < headers.Length; j++)
                row[j + 1] = j < values.Length ? values[j] : "";
            _rows.Add(row);
        }

        Grid.ItemsSource = _rows;
    }

    private async void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var selectedRows = Grid.SelectedItems.Cast<string[]>().ToList();
        if (selectedRows.Count == 0) return;

        var sb = new StringBuilder();
        var copyRow = CopyRowCheckBox.IsChecked == true;
        var includeHeader = IncludeHeaderCheckBox.IsChecked == true;

        if (copyRow)
        {
            if (includeHeader)
            {
                sb.AppendLine(string.Join('\t',
                    Grid.Columns.Select(c => c.Header?.ToString() ?? "")));
            }
            foreach (var row in selectedRows)
                sb.AppendLine(string.Join('\t', row));
        }
        else
        {
            // Cell mode: copy values from the current column only
            var col = Grid.CurrentColumn;
            if (col is null) return;

            var colIndex = Grid.Columns.IndexOf(col);
            if (colIndex < 0) return;

            if (includeHeader)
                sb.AppendLine(col.Header?.ToString() ?? "");

            foreach (var row in selectedRows)
                sb.AppendLine(colIndex < row.Length ? row[colIndex] : "");
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(sb.ToString().TrimEnd('\r', '\n'));

        e.Handled = true;
    }

    private void OnSearchModeChanged(object? sender, RoutedEventArgs e)
    {
        var byColumn = SearchModeToggle.IsChecked == true;
        SearchModeToggle.Content = byColumn ? _firstColumnHeader : "Row";
        SearchBox.Watermark = byColumn ? "Value + Enter" : "Row # + Enter";
        SearchBox.Text = "";
        _lastSearchIndex = -1;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _rows is null) return;
        var text = SearchBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        if (SearchModeToggle.IsChecked == true)
            SearchByFirstColumn(text);
        else
            GoToRow(text);
    }

    private void GoToRow(string text)
    {
        if (!int.TryParse(text, out var rowNum) || _rows is null) return;

        var index = rowNum - 1;
        if (index < 0 || index >= _rows.Count) return;

        Grid.SelectedIndex = index;
        Grid.ScrollIntoView(_rows[index], null);
        SearchBox.Focus();
    }

    private void SearchByFirstColumn(string text)
    {
        if (_rows is null || _rows.Count == 0) return;

        // Search from after the last match to cycle through results
        var startIndex = _lastSearchIndex + 1;
        for (int i = 0; i < _rows.Count; i++)
        {
            var index = (startIndex + i) % _rows.Count;
            var value = _rows[index][1]; // first data column is at index 1
            if (value.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                _lastSearchIndex = index;
                Grid.SelectedIndex = index;
                Grid.ScrollIntoView(_rows[index], null);
                SearchBox.Focus();
                return;
            }
        }
    }

    private void UpdateFrozenState()
    {
        if (_firstDataColumn is null || _rows is null) return;

        var locked = FreezeCheckBox.IsChecked == true;
        Grid.FrozenColumnCount = locked ? 2 : 1;

        if (locked)
            _firstDataColumn.CellStyleClasses.Add("frozen");
        else
            _firstDataColumn.CellStyleClasses.Remove("frozen");

        // Force cell refresh so style class change takes effect
        Grid.ItemsSource = null;
        Grid.ItemsSource = _rows;
    }
}
