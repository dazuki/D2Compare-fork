using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridConditionalFormatting;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2Compare.Controls;
using D2Compare.Core.Models;
using D2Compare.Core.Services;
using D2Compare.Services;
using D2Compare.Views;
using Microsoft.Win32;

namespace D2Compare.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TopLevel _topLevel;

    [ObservableProperty]
    private ObservableCollection<string> _sourceVersions = new();

    [ObservableProperty]
    private ObservableCollection<string> _targetVersions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceCustom))]
    private int _selectedSourceIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetCustom))]
    private int _selectedTargetIndex = -1;

    [ObservableProperty]
    private ObservableCollection<string> _fileList = new();

    [ObservableProperty]
    private int _selectedFileIndex = -1;

    [ObservableProperty]
    private bool _includeNewRows;

    [ObservableProperty]
    private bool _showOnlyNewRows;

    [ObservableProperty]
    private bool _omitUnchangedFiles = true;

    [ObservableProperty]
    private bool _batchReloadNeeded;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private bool _convertColumns = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowModeNone))]
    [NotifyPropertyChangedFor(nameof(IsRowModeAppendOriginal))]
    [NotifyPropertyChangedFor(nameof(IsRowModeAppendTarget))]
    private RowConversionMode _rowConversionMode = RowConversionMode.None;

    // No row conversion (keep source rows)
    public bool IsRowModeNone
    {
        get => RowConversionMode == RowConversionMode.None;
        set
        {
            if (value)
                RowConversionMode = RowConversionMode.None;
        }
    }

    // Append original data at end
    public bool IsRowModeAppendOriginal
    {
        get => RowConversionMode == RowConversionMode.AppendOriginalAtEnd;
        set
        {
            if (value)
                RowConversionMode = RowConversionMode.AppendOriginalAtEnd;
        }
    }

    // Append target data at end
    public bool IsRowModeAppendTarget
    {
        get => RowConversionMode == RowConversionMode.AppendTargetAtEnd;
        set
        {
            if (value)
                RowConversionMode = RowConversionMode.AppendTargetAtEnd;
        }
    }

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isStatusWarning;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _matchLabel = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private DataGridCollectionView? _columnsView;

    [ObservableProperty]
    private DataGridCollectionView? _rowsView;

    [ObservableProperty]
    private DataGridCollectionView? _valuesView;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveColumnsAsTextCommand))]
    private bool _columnsHasContent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRowsAsTextCommand))]
    private bool _rowsHasContent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveValuesAsTextCommand))]
    private bool _valuesHasContent;

    [ObservableProperty]
    private bool _isBatchView;

    // True while a search narrows the grids to matching rows
    [ObservableProperty]
    private bool _isSearchFiltered;

    public HighlightOnlySearchModel ColumnsSearch { get; } = new(SearchHighlightMode.TextAndCell);
    public HighlightOnlySearchModel RowsSearch { get; } = new(SearchHighlightMode.TextAndCell);
    public HighlightOnlySearchModel ValuesSearch { get; } = new(SearchHighlightMode.TextAndCell);

    public ConditionalFormattingModel ColumnsFormatting { get; } = CreateChangeKindFormatting();
    public ConditionalFormattingModel RowsFormatting { get; } = CreateChangeKindFormatting();

    [ObservableProperty]
    private FormattedDocument? _filesDocument;

    [ObservableProperty]
    private int _columnsAdded;

    [ObservableProperty]
    private int _columnsRemoved;

    [ObservableProperty]
    private int _columnsChanged;

    [ObservableProperty]
    private int _rowsAdded;

    [ObservableProperty]
    private int _rowsRemoved;

    [ObservableProperty]
    private int _rowsChanged;

    [ObservableProperty]
    private int _valuesChanged;

    [ObservableProperty]
    private int _valuesNew;

    public bool HasNoFileChanges => FilesDocument is null || FilesDocument.Lines.Count == 0;

    partial void OnFilesDocumentChanged(FormattedDocument? value) =>
        OnPropertyChanged(nameof(HasNoFileChanges));

    public AppSettings Settings => _settings;
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<VersionInfo> _availableVersions;

    private string _sourceFolderPath = "";
    private string _targetFolderPath = "";
    private List<CompareResult> _batchResults = new();

    // Full rows of the current comparison; a search filters these into the visible rows
    private List<SchemaDiffRow> _allColumnRows = [];
    private List<SchemaDiffRow> _allRowRows = [];
    private List<ValueDiffRow> _allValueRows = [];

    // What the grids show right now; Export Summary writes these
    private List<SchemaDiffRow> _visibleColumnRows = [];
    private List<SchemaDiffRow> _visibleRowRows = [];
    private List<ValueDiffRow> _visibleValueRows = [];
    private string _activeSearchTerm = "";

    // Bumped by every new comparison so results from a superseded run are dropped
    private int _comparisonGeneration;

    public string AppVersion => UpdateService.GetCurrentVersion();
    public bool IsSourceCustom => SelectedSourceIndex >= _availableVersions.Count;
    public bool IsTargetCustom => SelectedTargetIndex >= _availableVersions.Count;

    // Auto-update
    [ObservableProperty]
    private UpdateInfo? _pendingUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private string _updateStatusText = "";

    public bool HasPendingUpdate => PendingUpdate is not null;
    public string UpdateAvailableText =>
        PendingUpdate is null ? "" : $"Update available: {PendingUpdate.TagName}";

    partial void OnPendingUpdateChanged(UpdateInfo? value)
    {
        OnPropertyChanged(nameof(HasPendingUpdate));
        OnPropertyChanged(nameof(UpdateAvailableText));
    }

    public MainViewModel(TopLevel topLevel)
    {
        _topLevel = topLevel;
        _settings = AppSettings.Load();

        IsDarkMode = _settings.IsDarkMode;
        if (IsDarkMode && Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = ThemeVariant.Dark;

        _availableVersions = VersionInfo.GetAvailableVersions();

        var names = _availableVersions.Select(v => v.DisplayName).ToList();
        names.Add("Custom");

        SourceVersions = new ObservableCollection<string>(names);
        TargetVersions = new ObservableCollection<string>(names);

        var customIndex = _availableVersions.Count;

        // Restore custom paths before setting indices (only if directory still exists)
        if (
            !string.IsNullOrEmpty(_settings.CustomSourcePath)
            && Directory.Exists(_settings.CustomSourcePath)
        )
        {
            _sourceFolderPath = _settings.CustomSourcePath;
            SourceVersions[customIndex] = _settings.CustomSourcePath;
        }
        else if (!string.IsNullOrEmpty(_settings.CustomSourcePath))
        {
            _settings.CustomSourcePath = "";
            if (_settings.SelectedSource == AppSettings.CustomSelection)
                _settings.SelectedSource = null;
            _settings.Save();
            StatusText = $"Custom source folder no longer exists";
            IsStatusWarning = true;
        }

        if (
            !string.IsNullOrEmpty(_settings.CustomTargetPath)
            && Directory.Exists(_settings.CustomTargetPath)
        )
        {
            _targetFolderPath = _settings.CustomTargetPath;
            TargetVersions[customIndex] = _settings.CustomTargetPath;
        }
        else if (!string.IsNullOrEmpty(_settings.CustomTargetPath))
        {
            _settings.CustomTargetPath = "";
            if (_settings.SelectedTarget == AppSettings.CustomSelection)
                _settings.SelectedTarget = null;
            _settings.Save();
            StatusText = $"Custom target folder no longer exists";
            IsStatusWarning = true;
        }

        var savedSource = ResolveSelection(_settings.SelectedSource);
        if (
            savedSource >= 0
            && (savedSource < customIndex || !string.IsNullOrEmpty(_settings.CustomSourcePath))
        )
            SelectedSourceIndex = savedSource;

        var savedTarget = ResolveSelection(_settings.SelectedTarget);
        if (
            savedTarget >= 0
            && (savedTarget < customIndex || !string.IsNullOrEmpty(_settings.CustomTargetPath))
        )
            SelectedTargetIndex = savedTarget;

        ApplyDefaultVersions();

        foreach (var search in AllSearchModels)
            search.ResultsChanged += (_, _) => UpdateMatchLabel();

        if (!_settings.DisableUpdateCheck)
            _ = CheckForUpdateAsync();
    }

    private const string DefaultSourceFolder = "92777a";

    // Only fills selections that weren't restored from settings
    private void ApplyDefaultVersions()
    {
        if (SelectedSourceIndex < 0)
        {
            var index = ResolveSelection(DefaultSourceFolder);
            if (index >= 0)
                SelectedSourceIndex = index;
        }

        if (SelectedTargetIndex < 0)
        {
            var index = _availableVersions
                .ToList()
                .FindLastIndex(v => v.DisplayName.EndsWith("- RoTW", StringComparison.Ordinal));
            if (index >= 0)
                SelectedTargetIndex = index;
        }
    }

    // Maps a saved selection (folder name or "custom") to a dropdown index, -1 if unknown
    private int ResolveSelection(string? selection)
    {
        if (string.IsNullOrEmpty(selection))
            return -1;
        if (selection == AppSettings.CustomSelection)
            return _availableVersions.Count;

        return _availableVersions
            .ToList()
            .FindIndex(v =>
                string.Equals(v.FolderName, selection, StringComparison.OrdinalIgnoreCase)
            );
    }

    private string ToSelection(int index) =>
        index < _availableVersions.Count
            ? _availableVersions[index].FolderName
            : AppSettings.CustomSelection;

    partial void OnSelectedSourceIndexChanged(int value)
    {
        if (value < 0)
            return;

        if (value < _availableVersions.Count)
            _sourceFolderPath = _availableVersions[value].GetPath();
        else if (!string.IsNullOrEmpty(_settings.CustomSourcePath))
            _sourceFolderPath = _settings.CustomSourcePath;

        _settings.SelectedSource = ToSelection(value);
        _settings.Save();
        OnTargetChanged();
    }

    partial void OnSelectedTargetIndexChanged(int value)
    {
        if (value < 0)
            return;

        if (value < _availableVersions.Count)
            _targetFolderPath = _availableVersions[value].GetPath();
        else if (!string.IsNullOrEmpty(_settings.CustomTargetPath))
            _targetFolderPath = _settings.CustomTargetPath;

        _settings.SelectedTarget = ToSelection(value);
        _settings.Save();
        OnTargetChanged();
    }

    partial void OnSelectedFileIndexChanged(int value)
    {
        OpenSourceFileCommand.NotifyCanExecuteChanged();
        OpenTargetFileCommand.NotifyCanExecuteChanged();
        if (value < 0 || value >= FileList.Count)
            return;
        BatchReloadNeeded = false;
        _ = RunSingleComparisonAsync();
    }

    partial void OnIncludeNewRowsChanged(bool value)
    {
        if (!value)
            ShowOnlyNewRows = false;
        if (SelectedFileIndex >= 0 && _sourceFolderPath.Length > 0)
            _ = RunSingleComparisonAsync();
        else if (_batchResults.Count > 0)
            BatchReloadNeeded = true;
    }

    partial void OnShowOnlyNewRowsChanged(bool value)
    {
        if (SelectedFileIndex >= 0 && _sourceFolderPath.Length > 0)
            _ = RunSingleComparisonAsync();
        else if (_batchResults.Count > 0)
            BatchReloadNeeded = true;
    }

    partial void OnOmitUnchangedFilesChanged(bool value)
    {
        if (_batchResults.Count > 0 && SelectedFileIndex < 0)
            BatchReloadNeeded = true;
    }

    private HighlightOnlySearchModel[] AllSearchModels => [ColumnsSearch, RowsSearch, ValuesSearch];

    // Colors the Change column; theme keys resolve from MainWindow resources
    private static ConditionalFormattingModel CreateChangeKindFormatting()
    {
        var model = new ConditionalFormattingModel();
        model.Apply([
            ChangeKindRule(ChangeKind.Added, "AddedCellTheme"),
            ChangeKindRule(ChangeKind.Removed, "RemovedCellTheme"),
            ChangeKindRule(ChangeKind.Changed, "ChangedCellTheme"),
            ChangeKindRule(ChangeKind.Unchanged, "MutedCellTheme"),
        ]);
        return model;
    }

    private static ConditionalFormattingDescriptor ChangeKindRule(
        ChangeKind kind,
        string themeKey
    ) =>
        new(
            ruleId: kind,
            @operator: ConditionalFormattingOperator.Equals,
            columnId: nameof(SchemaDiffRow.Change),
            value: kind.ToString(),
            themeKey: themeKey
        );

    [RelayCommand]
    private async Task ExecuteSearch()
    {
        var term = SearchText.Trim();
        if (term.Length == 0)
        {
            ClearSearch();
            return;
        }

        var generation = _comparisonGeneration;
        var (allColumns, allRows, allValues) = (_allColumnRows, _allRowRows, _allValueRows);
        IsLoading = true;
        StatusText = "Searching...";

        try
        {
            var (columns, rows, values) = await Task.Run(() =>
                (
                    DiffRowBuilder.Filter(allColumns, term),
                    DiffRowBuilder.Filter(allRows, term),
                    DiffRowBuilder.Filter(allValues, term)
                )
            );
            if (generation != _comparisonGeneration)
                return;

            _activeSearchTerm = term;
            IsSearchFiltered = true;
            ShowRows(columns, rows, values);

            // Highlights the term inside the remaining cells
            var descriptor = new SearchDescriptor(
                term,
                comparison: StringComparison.OrdinalIgnoreCase
            );
            foreach (var search in AllSearchModels)
                search.SetOrUpdate(descriptor);
            UpdateMatchLabel();
        }
        finally
        {
            if (generation == _comparisonGeneration)
            {
                IsLoading = false;
                StatusText = "";
            }
        }
    }

    private void ClearSearch()
    {
        foreach (var search in AllSearchModels)
            search.Clear();
        MatchLabel = "";

        if (IsSearchFiltered)
        {
            IsSearchFiltered = false;
            ShowRows(_allColumnRows, _allRowRows, _allValueRows);
        }
    }

    private void UpdateMatchLabel()
    {
        if (!IsSearchFiltered)
        {
            MatchLabel = "";
            return;
        }
        var matches = AllSearchModels.Sum(s => s.Results.Count);
        var rows = _visibleColumnRows.Count + _visibleRowRows.Count + _visibleValueRows.Count;
        MatchLabel = $"{matches} matches in {rows} rows";
    }

    [RelayCommand]
    private async Task BatchLoad()
    {
        if (_sourceFolderPath.Length == 0 || _targetFolderPath.Length == 0)
            return;
        if (!Directory.Exists(_sourceFolderPath) || !Directory.Exists(_targetFolderPath))
        {
            StatusText = "One or both folder paths no longer exist";
            IsStatusWarning = true;
            return;
        }

        var generation = ++_comparisonGeneration;
        IsLoading = true;
        IsStatusWarning = false;
        StatusText = "Loading...";

        try
        {
            var sourcePath = _sourceFolderPath;
            var targetPath = _targetFolderPath;
            var includeNew = IncludeNewRows;

            var results = await Task.Run(() =>
                CompareService.CompareFolder(sourcePath, targetPath, includeNew)
            );
            if (generation != _comparisonGeneration)
                return;

            _batchResults = results;
            SelectedFileIndex = -1;
            ShowResults(results, isBatch: true);
            BatchReloadNeeded = false;
            SearchText = "";
        }
        catch (Exception ex)
        {
            if (generation != _comparisonGeneration)
                return;
            StatusText = $"Batch load failed: {ex.Message}";
            IsStatusWarning = true;
        }
        finally
        {
            if (generation == _comparisonGeneration)
            {
                IsLoading = false;
                if (!IsStatusWarning)
                    StatusText = "";
            }
        }
    }

    [RelayCommand]
    private void BrowseSourceCustom() => _ = SelectCustomFolder(isSource: true);

    [RelayCommand]
    private void BrowseTargetCustom() => _ = SelectCustomFolder(isSource: false);

    [RelayCommand]
    private async Task ConvertToTarget()
    {
        if (_sourceFolderPath.Length == 0 || _targetFolderPath.Length == 0)
        {
            StatusText = "Select source and target folders first";
            IsStatusWarning = true;
            return;
        }
        if (!Directory.Exists(_sourceFolderPath) || !Directory.Exists(_targetFolderPath))
        {
            StatusText = "One or both folder paths no longer exist";
            IsStatusWarning = true;
            return;
        }
        if (!ConvertColumns)
        {
            StatusText = "Select at least one conversion option (columns)";
            IsStatusWarning = true;
            return;
        }

        var storage = _topLevel.StorageProvider;
        if (!storage.CanPickFolder)
            return;

        var folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Output Folder for Converted Files",
                AllowMultiple = false,
            }
        );

        if (folders.Count == 0)
            return;

        var outputPath = folders[0].Path.LocalPath;
        var sourcePath = _sourceFolderPath;
        var targetPath = _targetFolderPath;
        var convertColumns = ConvertColumns;
        var rowMode = RowConversionMode;
        IsLoading = true;
        IsStatusWarning = false;
        StatusText = "Converting...";

        try
        {
            // Off the UI thread; progress is marshalled back so the status bar keeps updating
            await Task.Run(() =>
                ConvertService.ConvertFolder(
                    sourcePath,
                    targetPath,
                    outputPath,
                    convertColumns,
                    rowMode,
                    name => Dispatcher.UIThread.Post(() => StatusText = $"Converting {name}...")
                )
            );
            StatusText = $"Done. Files written to {outputPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Conversion failed: {ex.Message}";
            IsStatusWarning = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;

        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

        _settings.IsDarkMode = IsDarkMode;
        _settings.Save();
    }

    [RelayCommand]
    private void OpenOriginalProject() => OpenUrl("https://github.com/locbones/D2Compare");

    private bool HasFileSelected() => SelectedFileIndex >= 0 && SelectedFileIndex < FileList.Count;

    [RelayCommand(CanExecute = nameof(HasFileSelected))]
    private void OpenSourceFile()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count)
            return;

        var filePath = Path.Combine(_sourceFolderPath, FileList[SelectedFileIndex]);
        OpenFileViewer(filePath, role: "--source", originPath: _sourceFolderPath);
    }

    [RelayCommand(CanExecute = nameof(HasFileSelected))]
    private void OpenTargetFile()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count)
            return;

        var filePath = Path.Combine(_targetFolderPath, FileList[SelectedFileIndex]);
        OpenFileViewer(filePath, role: "--target", originPath: _targetFolderPath);
    }

    private void OpenFileViewer(string filePath, string role, string originPath)
    {
        if (!File.Exists(filePath))
            return;

        string absFilePath = Path.GetFullPath(filePath);
        string absOriginPath = Path.GetFullPath(originPath);

        string? viewerPath = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            viewerPath = GetWindowsViewerPathFromRegistry();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            viewerPath = _settings.LinuxViewerPath;

        ProcessStartInfo psi;
        if (!string.IsNullOrWhiteSpace(viewerPath) && File.Exists(viewerPath))
        {
            psi = new ProcessStartInfo { FileName = viewerPath, UseShellExecute = false };
            foreach (var arg in new[] { role, "--path", absOriginPath, absFilePath })
                psi.ArgumentList.Add(arg);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            psi = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
            psi.ArgumentList.Add(absFilePath);
        }
        else
        {
            psi = new ProcessStartInfo { FileName = absFilePath, UseShellExecute = true };
        }

        TryStartProcess(psi, $"Could not open {Path.GetFileName(filePath)}");
    }

    private void TryStartProcess(ProcessStartInfo psi, string failureMessage)
    {
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusText = $"{failureMessage}: {ex.Message}";
            IsStatusWarning = true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsViewerPathFromRegistry()
    {
        const string valueName = "ExecutablePath";
        const string KeyPath = @"Software\d2_horadrim";
        using (var legacyKey = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false))
        {
            return legacyKey?.GetValue(valueName) as string;
        }
    }

    private void ShowResults(IReadOnlyList<CompareResult> results, bool isBatch)
    {
        var includeUnchanged = isBatch && !OmitUnchangedFiles;
        _allColumnRows = DiffRowBuilder.BuildColumnRows(results, includeUnchanged);
        _allRowRows = DiffRowBuilder.BuildRowRows(results, includeUnchanged);
        _allValueRows = DiffRowBuilder.BuildValueRows(results, ShowOnlyNewRows);

        foreach (var search in AllSearchModels)
            search.Clear();
        MatchLabel = "";
        IsSearchFiltered = false;
        IsBatchView = isBatch;
        ShowRows(_allColumnRows, _allRowRows, _allValueRows);
        UpdateStats(results);
    }

    private void ClearResults()
    {
        _allColumnRows = [];
        _allRowRows = [];
        _allValueRows = [];
        ClearSearch();
        ShowRows([], [], []);
    }

    private void ShowRows(
        List<SchemaDiffRow> columnRows,
        List<SchemaDiffRow> rowRows,
        List<ValueDiffRow> valueRows
    )
    {
        _visibleColumnRows = columnRows;
        _visibleRowRows = rowRows;
        _visibleValueRows = valueRows;

        var batch = IsBatchView;
        ColumnsView = CreateView(columnRows, batch ? DiffRowBuilder.Summarize(columnRows) : null);
        RowsView = CreateView(rowRows, batch ? DiffRowBuilder.Summarize(rowRows) : null);
        ValuesView = CreateView(
            valueRows,
            batch ? DiffRowBuilder.Summarize(valueRows) : null,
            nameof(ValueDiffRow.RowLabel)
        );
        ColumnsHasContent = columnRows.Count > 0;
        RowsHasContent = rowRows.Count > 0;
        ValuesHasContent = valueRows.Count > 0;
    }

    private static DataGridCollectionView CreateView<T>(
        List<T> rows,
        IReadOnlyDictionary<string, FileSummary>? fileSummaries,
        string? subGroupPath = null
    )
    {
        // Without isDataInGroupOrder each row searches all existing groups, which takes 10s+
        // for the Values panel's thousands of row groups. That mode splits groups once a column
        // sort reorders rows, so the Values grid (the only one with row groups) can't sort.
        var view = new DataGridCollectionView(
            rows,
            isDataSorted: false,
            isDataInGroupOrder: subGroupPath is not null
        );
        using (view.DeferRefresh())
        {
            if (fileSummaries is not null)
                view.GroupDescriptions.Add(new FileGroupDescription(fileSummaries));
            if (subGroupPath is not null)
                view.GroupDescriptions.Add(new DataGridPathGroupDescription(subGroupPath));
        }
        // No initial current item, otherwise the grid preselects the first row
        view.MoveCurrentToPosition(-1);
        return view;
    }

    private async Task RunSingleComparisonAsync()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count)
            return;

        var sourcePath = Path.Combine(_sourceFolderPath, FileList[SelectedFileIndex]);
        var targetPath = Path.Combine(_targetFolderPath, FileList[SelectedFileIndex]);

        if (!File.Exists(sourcePath) || !File.Exists(targetPath))
        {
            StatusText = "File not found";
            IsStatusWarning = true;
            return;
        }

        var generation = ++_comparisonGeneration;
        IsLoading = true;
        IsStatusWarning = false;
        StatusText = "Loading...";
        var includeNew = IncludeNewRows;

        try
        {
            var result = await Task.Run(() =>
                CompareService.CompareFile(sourcePath, targetPath, includeNew)
            );
            if (generation != _comparisonGeneration)
                return;

            ShowResults([result], isBatch: false);

            SearchText = "";
        }
        catch (Exception ex)
        {
            if (generation != _comparisonGeneration)
                return;
            StatusText = $"Comparison failed: {ex.Message}";
            IsStatusWarning = true;
        }
        finally
        {
            if (generation == _comparisonGeneration)
            {
                IsLoading = false;
                if (!IsStatusWarning)
                    StatusText = "";
            }
        }
    }

    private void UpdateStats(IEnumerable<CompareResult> results)
    {
        int ca = 0,
            cr = 0,
            cc = 0,
            ra = 0,
            rr = 0,
            rc = 0,
            vc = 0,
            vn = 0;
        foreach (var r in results)
        {
            ca += r.AddedColumns.Count;
            cr += r.RemovedColumns.Count;
            cc += r.ChangedColumns.Count;
            ra += r.AddedRows.Count;
            rr += r.RemovedRows.Count;
            rc += r.ChangedRows.Count;
            foreach (var g in r.GroupedDifferences)
            {
                if (g.IsNew)
                    vn++;
                else
                    vc++;
            }
        }
        ColumnsAdded = ca;
        ColumnsRemoved = cr;
        ColumnsChanged = cc;
        RowsAdded = ra;
        RowsRemoved = rr;
        RowsChanged = rc;
        ValuesChanged = ShowOnlyNewRows ? 0 : vc;
        ValuesNew = vn;
    }

    private void OnTargetChanged()
    {
        if (_sourceFolderPath.Length == 0 || _targetFolderPath.Length == 0)
            return;
        if (!Directory.Exists(_sourceFolderPath) || !Directory.Exists(_targetFolderPath))
            return;

        _comparisonGeneration++;
        IsLoading = false;
        IsStatusWarning = false;
        StatusText = "";
        var fileResult = FileDiscovery.DiscoverFiles(_sourceFolderPath, _targetFolderPath);

        FileList = new ObservableCollection<string>(fileResult.CommonFiles);
        FilesDocument = FormattedTextBuilder.BuildFileDiffs(fileResult);
        SelectedFileIndex = -1;
        ClearResults();
    }

    private async Task SelectCustomFolder(bool isSource)
    {
        var storage = _topLevel.StorageProvider;
        if (!storage.CanPickFolder)
            return;

        var folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = isSource ? "Select Source Folder" : "Select Target Folder",
                AllowMultiple = false,
            }
        );

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            var customIndex = _availableVersions.Count;

            if (isSource)
            {
                _sourceFolderPath = path;
                SourceVersions[customIndex] = path;
                _settings.SelectedSource = AppSettings.CustomSelection;
                _settings.CustomSourcePath = path;
                _settings.Save();
                SelectedSourceIndex = customIndex;
                OnPropertyChanged(nameof(IsSourceCustom));
            }
            else
            {
                _targetFolderPath = path;
                TargetVersions[customIndex] = path;
                _settings.SelectedTarget = AppSettings.CustomSelection;
                _settings.CustomTargetPath = path;
                _settings.Save();
                SelectedTargetIndex = customIndex;
                OnPropertyChanged(nameof(IsTargetCustom));
            }

            OnTargetChanged();
        }
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await UpdateService.CheckForUpdateAsync();
        if (info is not null)
            PendingUpdate = info;
    }

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        if (PendingUpdate is null)
            return;

        var dialog = new UpdateDialog(PendingUpdate.Version, PendingUpdate.ReleaseNotes);
        var result = await dialog.ShowDialog<bool?>((Window)_topLevel);
        if (result is not true)
            return;

        IsDownloadingUpdate = true;
        UpdateStatusText = "Downloading...";
        var progress = new Progress<double>(p =>
        {
            UpdateDownloadProgress = p;
            UpdateStatusText = $"Downloading... {p:P0}";
        });

        var staged = await UpdateService.DownloadUpdateAsync(PendingUpdate, progress);
        if (staged is null)
        {
            UpdateStatusText = "Download failed";
            IsDownloadingUpdate = false;
            return;
        }

        UpdateStatusText = "Applying update...";
        UpdateService.LaunchUpdateScriptAndExit(staged);
    }

    [RelayCommand]
    private void DismissUpdate() => PendingUpdate = null;

    [RelayCommand(CanExecute = nameof(ColumnsHasContent))]
    private Task SaveColumnsAsText() =>
        SaveLinesAsText(
            DiffRowTextFormatter.FormatSchemaRows(_visibleColumnRows, "Col", IsBatchView),
            "Columns.Altered",
            "Columns Altered"
        );

    [RelayCommand(CanExecute = nameof(RowsHasContent))]
    private Task SaveRowsAsText() =>
        SaveLinesAsText(
            DiffRowTextFormatter.FormatSchemaRows(_visibleRowRows, "Row", IsBatchView),
            "Rows.Altered",
            "Rows Altered"
        );

    [RelayCommand(CanExecute = nameof(ValuesHasContent))]
    private Task SaveValuesAsText() =>
        SaveLinesAsText(
            DiffRowTextFormatter.FormatValueRows(_visibleValueRows, IsBatchView),
            "Values",
            "Values"
        );

    // Writes what the panel currently shows, so an active search filter is exported as-is
    private async Task SaveLinesAsText(
        IEnumerable<string> lines,
        string panelSuffix,
        string panelName
    )
    {
        bool isDisplayAll = _batchResults.Count > 0 && SelectedFileIndex < 0;
        string prefix;
        if (isDisplayAll)
            prefix = "DisplayAll";
        else if (SelectedFileIndex >= 0 && SelectedFileIndex < FileList.Count)
            prefix = Path.GetFileNameWithoutExtension(FileList[SelectedFileIndex]);
        else
            prefix = "output";

        var storage = _topLevel.StorageProvider;
        var file = await storage.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Save As Text",
                SuggestedFileName = $"{prefix}-{panelSuffix}.txt",
                FileTypeChoices = [new FilePickerFileType("Text File") { Patterns = ["*.txt"] }],
            }
        );

        if (file is null)
            return;

        var sourceName =
            SelectedSourceIndex >= 0 && SelectedSourceIndex < SourceVersions.Count
                ? SourceVersions[SelectedSourceIndex]
                : "Unknown";
        var targetName =
            SelectedTargetIndex >= 0 && SelectedTargetIndex < TargetVersions.Count
                ? TargetVersions[SelectedTargetIndex]
                : "Unknown";

        var headerLines = new List<string> { $"Source:   {sourceName}", $"Target:   {targetName}" };
        headerLines.Add($"File:     {(isDisplayAll ? "All Files" : $"{prefix}.txt")}");
        headerLines.Add($"Summary:  {panelName}");
        if (IsSearchFiltered)
            headerLines.Add($"Search:   {_activeSearchTerm}");
        headerLines.Add(new string('-', 40));
        headerLines.Add("");

        try
        {
            await File.WriteAllLinesAsync(file.Path.LocalPath, headerLines.Concat(lines));
            IsStatusWarning = false;
            StatusText = $"Saved {Path.GetFileName(file.Path.LocalPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            IsStatusWarning = true;
        }
    }

    public void InitializeFromArguments(
        string? sourceFolder,
        string? targetFolder,
        string? filePath
    )
    {
        var customIndex = _availableVersions.Count;

        if (!string.IsNullOrWhiteSpace(sourceFolder) && Directory.Exists(sourceFolder))
        {
            // Treat as custom source folder
            _settings.CustomSourcePath = sourceFolder;
            _settings.SelectedSource = AppSettings.CustomSelection;
            _settings.Save();

            if (SourceVersions.Count > customIndex)
                SourceVersions[customIndex] = sourceFolder;

            _sourceFolderPath = sourceFolder;
            SelectedSourceIndex = customIndex;
        }

        if (!string.IsNullOrWhiteSpace(targetFolder) && Directory.Exists(targetFolder))
        {
            // Treat as custom target folder
            _settings.CustomTargetPath = targetFolder;
            _settings.SelectedTarget = AppSettings.CustomSelection;
            _settings.Save();

            if (TargetVersions.Count > customIndex)
                TargetVersions[customIndex] = targetFolder;

            _targetFolderPath = targetFolder;
            SelectedTargetIndex = customIndex;
        }

        // If a specific file was provided, try to select and compare it
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            // Ensure the file list is built if we have valid folders
            if (
                FileList.Count == 0
                && _sourceFolderPath.Length > 0
                && _targetFolderPath.Length > 0
                && Directory.Exists(_sourceFolderPath)
                && Directory.Exists(_targetFolderPath)
            )
            {
                OnTargetChanged();
            }

            if (FileList.Count > 0)
            {
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = filePath;

                var index = FileList.IndexOf(fileName);
                if (index >= 0)
                    SelectedFileIndex = index;
            }
        }
    }

    private void OpenUrl(string url) =>
        TryStartProcess(
            new ProcessStartInfo { FileName = url, UseShellExecute = true },
            "Could not open browser"
        );
}
