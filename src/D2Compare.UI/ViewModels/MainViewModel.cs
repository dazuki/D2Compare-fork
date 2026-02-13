using System.Collections.ObjectModel;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TopLevel _topLevel;

    [ObservableProperty] private ObservableCollection<string> _sourceVersions = new();
    [ObservableProperty] private ObservableCollection<string> _targetVersions = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceCustom))]
    private int _selectedSourceIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetCustom))]
    private int _selectedTargetIndex = -1;

    [ObservableProperty] private ObservableCollection<string> _fileList = new();
    [ObservableProperty] private int _selectedFileIndex = -1;

    [ObservableProperty] private bool _includeNewRows;
    [ObservableProperty] private bool _omitUnchangedFiles = true;
    [ObservableProperty] private bool _isDarkMode;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _activeSearchTerm = "";
    [ObservableProperty] private string _matchLabel = "";
    [ObservableProperty] private bool _isLoading;

    private CancellationTokenSource? _searchDebounce;

    [ObservableProperty] private FormattedDocument? _columnsDocument;
    [ObservableProperty] private FormattedDocument? _rowsDocument;
    [ObservableProperty] private FormattedDocument? _valuesDocument;
    [ObservableProperty] private FormattedDocument? _filesDocument;

    public bool HasNoFileChanges => FilesDocument is null || FilesDocument.Lines.Count == 0;

    partial void OnFilesDocumentChanged(FormattedDocument? value) => OnPropertyChanged(nameof(HasNoFileChanges));

    public AppSettings Settings => _settings;
    private readonly AppSettings _settings;

    private string _sourceFolderPath = "";
    private string _targetFolderPath = "";
    private List<CompareResult> _batchResults = new();

    public string AppVersion => "2.0.0 (fork by dazuki)";
    public bool IsSourceCustom => SelectedSourceIndex >= VersionInfo.BuiltInVersions.Length;
    public bool IsTargetCustom => SelectedTargetIndex >= VersionInfo.BuiltInVersions.Length;

    public MainViewModel(TopLevel topLevel)
    {
        _topLevel = topLevel;
        _settings = AppSettings.Load();

        IsDarkMode = _settings.IsDarkMode;
        if (IsDarkMode && Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = ThemeVariant.Dark;

        var names = VersionInfo.BuiltInVersions.Select(v => v.DisplayName).ToList();
        names.Add("Custom");

        SourceVersions = new ObservableCollection<string>(names);
        TargetVersions = new ObservableCollection<string>(names);

        var customIndex = VersionInfo.BuiltInVersions.Length;

        // Restore custom paths before setting indices
        if (!string.IsNullOrEmpty(_settings.CustomSourcePath))
        {
            _sourceFolderPath = _settings.CustomSourcePath;
            SourceVersions[customIndex] = _settings.CustomSourcePath;
        }
        if (!string.IsNullOrEmpty(_settings.CustomTargetPath))
        {
            _targetFolderPath = _settings.CustomTargetPath;
            TargetVersions[customIndex] = _settings.CustomTargetPath;
        }

        if (_settings.SelectedSourceIndex >= 0 && _settings.SelectedSourceIndex < SourceVersions.Count)
        {
            if (_settings.SelectedSourceIndex < customIndex || !string.IsNullOrEmpty(_settings.CustomSourcePath))
                SelectedSourceIndex = _settings.SelectedSourceIndex;
        }
        if (_settings.SelectedTargetIndex >= 0 && _settings.SelectedTargetIndex < TargetVersions.Count)
        {
            if (_settings.SelectedTargetIndex < customIndex || !string.IsNullOrEmpty(_settings.CustomTargetPath))
                SelectedTargetIndex = _settings.SelectedTargetIndex;
        }
    }

    partial void OnSelectedSourceIndexChanged(int value)
    {
        if (value < 0) return;

        if (value < VersionInfo.BuiltInVersions.Length)
            _sourceFolderPath = VersionInfo.BuiltInVersions[value].GetPath();

        _settings.SelectedSourceIndex = value;
        _settings.Save();
        OnTargetChanged();
    }

    partial void OnSelectedTargetIndexChanged(int value)
    {
        if (value < 0) return;

        if (value < VersionInfo.BuiltInVersions.Length)
            _targetFolderPath = VersionInfo.BuiltInVersions[value].GetPath();

        _settings.SelectedTargetIndex = value;
        _settings.Save();
        OnTargetChanged();
    }

    partial void OnSelectedFileIndexChanged(int value)
    {
        if (value < 0 || value >= FileList.Count) return;
        RunSingleComparison();
    }

    partial void OnIncludeNewRowsChanged(bool value)
    {
        if (_sourceFolderPath.Length > 0 && SelectedFileIndex >= 0)
            RunSingleComparison();
    }

    partial void OnOmitUnchangedFilesChanged(bool value)
    {
        if (_batchResults.Count > 0)
            RebuildBatchDocuments();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;

        if (string.IsNullOrEmpty(value))
        {
            ActiveSearchTerm = "";
            UpdateMatchCount();
            return;
        }

        _ = Task.Delay(300, token).ContinueWith(_ =>
        {
            ActiveSearchTerm = value;
            UpdateMatchCount();
        }, token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task BatchLoad()
    {
        if (_sourceFolderPath.Length == 0 || _targetFolderPath.Length == 0) return;

        IsLoading = true;
        StatusText = "Loading...";

        try
        {
            var sourcePath = _sourceFolderPath;
            var targetPath = _targetFolderPath;
            var includeNew = IncludeNewRows;
            var omitUnchanged = OmitUnchangedFiles;

            _batchResults = await Task.Run(() =>
                CompareService.CompareFolder(sourcePath, targetPath, includeNew));

            SelectedFileIndex = -1;
            RebuildBatchDocuments();
            SearchText = "";
        }
        finally
        {
            IsLoading = false;
            StatusText = "";
        }
    }

    [RelayCommand]
    private void BrowseSourceCustom() => _ = SelectCustomFolder(isSource: true);

    [RelayCommand]
    private void BrowseTargetCustom() => _ = SelectCustomFolder(isSource: false);

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
    private static void OpenOriginalProject() =>
        OpenUrl("https://github.com/locbones/D2Compare");

    [RelayCommand]
    private void OpenSourceFile()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count) return;
        var path = Path.Combine(_sourceFolderPath, FileList[SelectedFileIndex]);
        OpenFile(path);
    }

    [RelayCommand]
    private void OpenTargetFile()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count) return;
        var path = Path.Combine(_targetFolderPath, FileList[SelectedFileIndex]);
        OpenFile(path);
    }

    private void RebuildBatchDocuments()
    {
        var omit = OmitUnchangedFiles;

        var colResults = omit
            ? _batchResults.Where(r => r.ChangedColumns.Count > 0 || r.AddedColumns.Count > 0 || r.RemovedColumns.Count > 0)
            : _batchResults;
        var rowResults = omit
            ? _batchResults.Where(r => r.ChangedRows.Count > 0 || r.AddedRows.Count > 0 || r.RemovedRows.Count > 0)
            : _batchResults;
        var valResults = omit
            ? _batchResults.Where(r => r.GroupedDifferences.Count > 0)
            : _batchResults;

        ColumnsDocument = FormattedTextBuilder.MergeDocuments(
            colResults.Select(r => FormattedTextBuilder.BuildColumnDiffs(r, true)));
        RowsDocument = FormattedTextBuilder.MergeDocuments(
            rowResults.Select(r => FormattedTextBuilder.BuildRowDiffs(r, true)));
        ValuesDocument = FormattedTextBuilder.MergeDocuments(
            valResults.Select(r => FormattedTextBuilder.BuildValueDiffs(r, true)));
    }

    private void RunSingleComparison()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= FileList.Count) return;

        var sourcePath = Path.Combine(_sourceFolderPath, FileList[SelectedFileIndex]);
        var targetPath = Path.Combine(_targetFolderPath, FileList[SelectedFileIndex]);

        if (!File.Exists(sourcePath) || !File.Exists(targetPath))
        {
            StatusText = "File not found";
            return;
        }

        var result = CompareService.CompareFile(sourcePath, targetPath, IncludeNewRows);

        ColumnsDocument = FormattedTextBuilder.BuildColumnDiffs(result, false);
        RowsDocument = FormattedTextBuilder.BuildRowDiffs(result, false);
        ValuesDocument = FormattedTextBuilder.BuildValueDiffs(result, false);

        SearchText = "";
    }

    private void OnTargetChanged()
    {
        if (_sourceFolderPath.Length == 0 || _targetFolderPath.Length == 0) return;
        if (!Directory.Exists(_sourceFolderPath) || !Directory.Exists(_targetFolderPath)) return;

        var fileResult = FileDiscovery.DiscoverFiles(_sourceFolderPath, _targetFolderPath);

        FileList = new ObservableCollection<string>(fileResult.CommonFiles);
        FilesDocument = FormattedTextBuilder.BuildFileDiffs(fileResult);
        SelectedFileIndex = -1;
        ColumnsDocument = null;
        RowsDocument = null;
        ValuesDocument = null;
    }

    private async Task SelectCustomFolder(bool isSource)
    {
        var storage = _topLevel.StorageProvider;
        if (!storage.CanPickFolder) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = isSource ? "Select Source Folder" : "Select Target Folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            var customIndex = VersionInfo.BuiltInVersions.Length;

            if (isSource)
            {
                _sourceFolderPath = path;
                SourceVersions[customIndex] = path;
                SelectedSourceIndex = customIndex;
                _settings.SelectedSourceIndex = customIndex;
                _settings.CustomSourcePath = path;
                _settings.Save();
            }
            else
            {
                _targetFolderPath = path;
                TargetVersions[customIndex] = path;
                SelectedTargetIndex = customIndex;
                _settings.SelectedTargetIndex = customIndex;
                _settings.CustomTargetPath = path;
                _settings.Save();
            }
        }
    }

    private void UpdateMatchCount()
    {
        if (string.IsNullOrEmpty(SearchText) || ValuesDocument is null)
        {
            MatchLabel = "";
            return;
        }

        // Count matches across all value document text
        int count = 0;
        foreach (var line in ValuesDocument.Lines)
        {
            foreach (var span in line.Spans)
            {
                int pos = 0;
                while (pos < span.Text.Length)
                {
                    int idx = span.Text.IndexOf(SearchText, pos, StringComparison.OrdinalIgnoreCase);
                    if (idx == -1) break;
                    count++;
                    pos = idx + SearchText.Length;
                }
            }
        }

        MatchLabel = count > 0 ? $"{count} matches" : "0 matches";
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        OpenUrl(path);
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}