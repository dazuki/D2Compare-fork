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
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace D2Compare.Views;

public partial class FileViewerWindow : Window
{
    private DataGridTextColumn? _firstDataColumn;
    private List<string[]>? _rows;
    private string[] _headers = [];
    private Encoding _fileEncoding = Encoding.UTF8;
    private string _filePath = "";
    private string _originalFileName = "";
    private const int SearchAnyColumn = 0;
    private const int SearchRowNumber = 1;
    private const int SearchColumnOffset = 3; // after Any, Row#, separator
    private string[][]? _originalRows;
    private bool _lockFirstColumn;
    private bool _includeHeaders;
    private int _lastSearchIndex = -1;
    private DataGridCell? _highlightedCell;

    public FileViewerWindow()
    {
        InitializeComponent();
    }

    public FileViewerWindow(string filePath, string label = "") : this()
    {
        Title = string.IsNullOrEmpty(label)
            ? Path.GetFileName(filePath)
            : $"{Path.GetFileName(filePath)} - {label}";
        CreateBackupIfNeeded(filePath);
        LoadTsv(filePath);
        LockFirstMenuItem.Click += OnLockFirstClick;
        SearchColumnCombo.SelectionChanged += OnSearchColumnChanged;
        Grid.KeyDown += OnGridKeyDown;
        SearchBox.KeyDown += OnSearchKeyDown;
        SearchBox.TextChanged += OnSearchTextChanged;
        SaveMenuItem.Click += OnSaveClick;
        SaveAsMenuItem.Click += OnSaveAsClick;
        CopyCellsMenuItem.Click += (_, _) => CopyToClipboard(copyRows: false);
        CopyRowsMenuItem.Click += (_, _) => CopyToClipboard(copyRows: true);
        IncludeHeadersMenuItem.Click += OnIncludeHeadersClick;
        Grid.LoadingRow += OnLoadingRow;
        Grid.CellEditEnding += OnCellEditEnding;
    }

    private static void CreateBackupIfNeeded(string filePath)
    {
        var backupPath = filePath + ".backup";
        if (!File.Exists(backupPath))
            File.Copy(filePath, backupPath);
    }

    private static string DetectLineEndings(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (b == '\r')
            {
                var next = stream.ReadByte();
                return next == '\n' ? "CRLF" : "CR";
            }
            if (b == '\n')
                return "LF";
        }
        return "N/A";
    }

    private static (Encoding encoding, string name) DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        // Check for BOM markers
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true), "UTF-8 (BOM)");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, "UTF-16 LE (BOM)");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, "UTF-16 BE (BOM)");

        // No BOM — check if content is valid UTF-8
        bool hasHighBytes = false;
        bool validUtf8 = true;
        int i = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            if (b <= 0x7F) { i++; continue; }

            hasHighBytes = true;
            int expected;
            if ((b & 0xE0) == 0xC0) expected = 1;
            else if ((b & 0xF0) == 0xE0) expected = 2;
            else if ((b & 0xF8) == 0xF0) expected = 3;
            else { validUtf8 = false; break; }

            for (int j = 0; j < expected; j++)
            {
                i++;
                if (i >= bytes.Length || (bytes[i] & 0xC0) != 0x80) { validUtf8 = false; break; }
            }
            if (!validUtf8) break;
            i++;
        }

        if (!hasHighBytes)
            return (new UTF8Encoding(false), "ASCII / UTF-8 (no BOM)");
        if (validUtf8)
            return (new UTF8Encoding(false), "UTF-8 (no BOM)");

        return (Encoding.GetEncoding(1252), "Windows-1252");
    }

    private void LoadTsv(string filePath)
    {
        var (encoding, encodingName) = DetectEncoding(filePath);
        EncodingLabel.Text = encodingName;

        var lineEndingType = DetectLineEndings(filePath);
        EncodingLabel.Text = $"{encodingName} | {lineEndingType}";

        var lines = File.ReadAllLines(filePath, encoding);
        if (lines.Length == 0) return;

        var headers = lines[0].Split('\t');
        _headers = headers;
        _fileEncoding = encoding;
        _filePath = filePath;
        _originalFileName = Path.GetFileName(filePath);
        PopulateSearchCombo(headers);

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

        // Remaining columns (editable)
        for (int i = 1; i < headers.Length; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new Binding($"[{i + 1}]") { Mode = BindingMode.TwoWay },
                IsReadOnly = false
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

        _originalRows = LoadBackupRows(filePath, encoding);
        Grid.ItemsSource = _rows;
    }

    private static string[][] LoadBackupRows(string filePath, Encoding encoding)
    {
        var backupPath = filePath + ".backup";
        var path = File.Exists(backupPath) ? backupPath : filePath;
        var lines = File.ReadAllLines(path, encoding);
        if (lines.Length <= 1) return [];

        var headerCount = lines[0].Split('\t').Length;
        var rows = new string[lines.Length - 1][];
        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split('\t');
            var row = new string[headerCount + 1];
            row[0] = i.ToString();
            for (int j = 0; j < headerCount; j++)
                row[j + 1] = j < values.Length ? values[j] : "";
            rows[i - 1] = row;
        }
        return rows;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var copyRows = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _includeHeaders;
        CopyToClipboard(copyRows);
        e.Handled = true;
    }

    private async void CopyToClipboard(bool copyRows)
    {
        var selectedRows = Grid.SelectedItems.Cast<string[]>().ToList();
        if (selectedRows.Count == 0) return;

        var sb = new StringBuilder();

        if (copyRows)
        {
            if (_includeHeaders)
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

            foreach (var row in selectedRows)
                sb.AppendLine(colIndex < row.Length ? row[colIndex] : "");
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(sb.ToString().TrimEnd('\r', '\n'));
    }

    private void OnIncludeHeadersClick(object? sender, RoutedEventArgs e)
    {
        _includeHeaders = !_includeHeaders;
        IncludeHeadersMenuItem.Icon = _includeHeaders
            ? new CheckBox { IsChecked = true, IsHitTestVisible = false }
            : null;
        CopyCellsMenuItem.IsEnabled = !_includeHeaders;
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        ColorizeRow(e.Row);
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        // Post to let the two-way binding update the array first
        Dispatcher.UIThread.Post(() => ColorizeRow(e.Row), DispatcherPriority.Background);
    }

    private void ColorizeRow(DataGridRow row)
    {
        var rowIndex = row.Index;
        if (_originalRows is null || _rows is null) return;
        if (rowIndex < 0 || rowIndex >= _rows.Count || rowIndex >= _originalRows.Length) return;

        var current = _rows[rowIndex];
        var original = _originalRows[rowIndex];
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush? changedBrush = null;

        // Skip column 0 (row#) and column 1 (first data column - readonly)
        for (int colIdx = 2; colIdx < Grid.Columns.Count; colIdx++)
        {
            var content = Grid.Columns[colIdx].GetCellContent(row);
            if (content is TextBlock tb)
            {
                bool changed = colIdx < current.Length && colIdx < original.Length
                    && current[colIdx] != original[colIdx];
                if (changed)
                {
                    changedBrush ??= new SolidColorBrush(
                        isDark ? Color.Parse("#FF8C00") : Color.Parse("#B34700"));
                    tb.Foreground = changedBrush;
                    tb.FontWeight = FontWeight.SemiBold;
                }
                else
                {
                    tb.ClearValue(TextBlock.ForegroundProperty);
                    tb.ClearValue(TextBlock.FontWeightProperty);
                }
            }
        }
    }

    private void PopulateSearchCombo(string[] headers)
    {
        SearchColumnCombo.Items.Add(new ComboBoxItem { Content = "Any Row/Column" });
        SearchColumnCombo.Items.Add(new ComboBoxItem { Content = "Row Number" });
        SearchColumnCombo.Items.Add(new Separator());
        foreach (var header in headers)
            SearchColumnCombo.Items.Add(new ComboBoxItem { Content = header });
        SearchColumnCombo.SelectedIndex = 0;
    }

    private void OnSearchColumnChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = SearchColumnCombo.SelectedIndex;

        // Skip separator — jump to next valid item
        if (idx == 2)
        {
            SearchColumnCombo.SelectedIndex = 3;
            return;
        }

        SearchBox.Watermark = idx == SearchRowNumber ? "Row # + Enter" : "Search + Enter";
        SearchBox.Text = "";
        _lastSearchIndex = -1;
        ClearSearchHighlight();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            _lastSearchIndex = -1;
            ClearSearchHighlight();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _rows is null) return;
        var text = SearchBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        var selectedIdx = SearchColumnCombo.SelectedIndex;
        if (selectedIdx == SearchRowNumber)
            GoToRow(text);
        else if (selectedIdx == SearchAnyColumn)
            SearchColumns(text, null);
        else
            SearchColumns(text, selectedIdx - SearchColumnOffset + 1); // +1 for row# prefix in array
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

    /// <summary>
    /// Search rows. If colIndex is null, search all columns. Otherwise search specific column.
    /// colIndex is the array index in _rows (1-based, 0 = row number).
    /// </summary>
    private void SearchColumns(string text, int? colIndex)
    {
        if (_rows is null || _rows.Count == 0) return;

        var startIndex = _lastSearchIndex + 1;
        for (int i = 0; i < _rows.Count; i++)
        {
            var index = (startIndex + i) % _rows.Count;
            var row = _rows[index];

            if (colIndex is int col)
            {
                if (col < row.Length && row[col].Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    SelectAndScroll(index, col);
                    return;
                }
            }
            else
            {
                // Search all data columns (skip index 0 = row number)
                for (int c = 1; c < row.Length; c++)
                {
                    if (row[c].Contains(text, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectAndScroll(index, c);
                        return;
                    }
                }
            }
        }
    }

    private void ClearSearchHighlight()
    {
        if (_highlightedCell is not null)
        {
            _highlightedCell.ClearValue(DataGridCell.BackgroundProperty);
            _highlightedCell = null;
        }
    }

    private void SelectAndScroll(int index, int? arrayColIndex = null)
    {
        ClearSearchHighlight();

        _lastSearchIndex = index;
        Grid.SelectedIndex = index;

        DataGridColumn? matchedCol = null;
        if (arrayColIndex is int colIdx && colIdx < Grid.Columns.Count)
        {
            matchedCol = Grid.Columns[colIdx];
            Grid.CurrentColumn = matchedCol;
        }

        Grid.ScrollIntoView(_rows![index], matchedCol);

        // Highlight the matched cell after scroll completes
        if (matchedCol is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var content = matchedCol.GetCellContent(_rows![index]);
                if (content?.Parent is DataGridCell cell)
                {
                    var isDark = ActualThemeVariant == ThemeVariant.Dark;
                    cell.Background = new SolidColorBrush(
                        isDark ? Color.Parse("#1A3A2A") : Color.Parse("#D6EED6"));
                    _highlightedCell = cell;
                }
            }, DispatcherPriority.Background);
        }

        SearchBox.Focus();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_rows is null || _headers.Length == 0 || string.IsNullOrEmpty(_filePath)) return;

        using var writer = new StreamWriter(_filePath, false, _fileEncoding) { NewLine = "\r\n" };
        writer.WriteLine(string.Join('\t', _headers));
        foreach (var row in _rows)
            writer.WriteLine(string.Join('\t', row.AsSpan(1).ToArray()));
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (_rows is null || _headers.Length == 0) return;

        var storage = StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a Copy",
            SuggestedFileName = _originalFileName,
            FileTypeChoices =
            [
                new FilePickerFileType("Tab-delimited Text") { Patterns = ["*.txt"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        using var writer = new StreamWriter(stream, _fileEncoding) { NewLine = "\r\n" };

        await writer.WriteLineAsync(string.Join('\t', _headers));
        foreach (var row in _rows)
        {
            // Skip row[0] (row number), write data columns only
            await writer.WriteLineAsync(string.Join('\t', row.AsSpan(1).ToArray()));
        }
    }

    private void OnLockFirstClick(object? sender, RoutedEventArgs e)
    {
        _lockFirstColumn = !_lockFirstColumn;
        LockFirstMenuItem.Icon = _lockFirstColumn
            ? new CheckBox { IsChecked = true, IsHitTestVisible = false }
            : null;
        UpdateFrozenState();
    }

    private void UpdateFrozenState()
    {
        if (_firstDataColumn is null || _rows is null) return;

        Grid.FrozenColumnCount = _lockFirstColumn ? 2 : 1;

        if (_lockFirstColumn)
            _firstDataColumn.CellStyleClasses.Add("frozen");
        else
            _firstDataColumn.CellStyleClasses.Remove("frozen");

        // Force cell refresh so style class change takes effect
        Grid.ItemsSource = null;
        Grid.ItemsSource = _rows;
    }
}
