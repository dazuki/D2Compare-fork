using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Compare.Core.Models;

namespace D2Compare;

public class AppSettings
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Windows: %LOCALAPPDATA%\D2Compare. Linux: $XDG_CONFIG_HOME/D2Compare, default ~/.config.
    // DoNotVerify, otherwise a missing folder returns an empty path
    private static readonly string s_configPath = Path.Combine(
        Environment.GetFolderPath(
            s_isWindows
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify
        ),
        "D2Compare",
        "settings.json"
    );

    // Windows versions before 2.1.2 kept settings next to the exe
    private static readonly string s_legacyPath = Path.Combine(
        AppContext.BaseDirectory,
        "D2Compare.settings.json"
    );

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public const string CustomSelection = "custom";

    // v2.0.8 had 23 built-in versions, so its "Custom" entry sat at index 23
    private const int LegacyCustomIndex = 23;

    public bool IsDarkMode { get; set; }

    // Version folder name (e.g. "92777a") or CustomSelection
    public string? SelectedSource { get; set; }
    public string? SelectedTarget { get; set; }

    // Legacy (<= v2.0.8) dropdown indices, migrated on load and no longer written
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SelectedSourceIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SelectedTargetIndex { get; set; }

    public string? CustomSourcePath { get; set; }
    public string? CustomTargetPath { get; set; }

    public double WindowWidth { get; set; } = 1536;
    public double WindowHeight { get; set; } = 864;
    public bool IsMaximized { get; set; }

    // Center point of the screen the window was last on
    public int? ScreenX { get; set; }
    public int? ScreenY { get; set; }
    public double Column0Star { get; set; } = 1.0;
    public double Column2Star { get; set; } = 1.0;
    public double Column4Star { get; set; } = 1.5;

    // Batch mode file groups start collapsed per panel; true when missing so new users see it
    public bool ColumnsCollapsed { get; set; } = true;
    public bool RowsCollapsed { get; set; } = true;
    public bool ValuesCollapsed { get; set; } = true;

    public bool DisableUpdateCheck { get; set; }

    // Variable for D2 Horadrim binary path on Unix
    public string LinuxViewerPath { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        if (s_isWindows)
            MigrateLegacyFile(s_legacyPath, s_configPath);

        try
        {
            if (File.Exists(s_configPath))
            {
                var settings =
                    JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(s_configPath))
                    ?? new();
                settings.MigrateLegacyIndices();
                return settings;
            }
        }
        catch { }

        return new();
    }

    // Moves the old settings file to the new location and leaves a ".old" note in its place.
    // Runs on every launch, so it also covers manual installs over an old version.
    // An existing new file wins; the old one is still retired.
    internal static void MigrateLegacyFile(string legacyPath, string configPath)
    {
        if (!File.Exists(legacyPath))
            return;

        try
        {
            var legacyJson = File.ReadAllText(legacyPath);
            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllText(configPath, legacyJson);
            }

            var note =
                $"D2Compare settings have moved to:\r\n{configPath}\r\n\r\n"
                + "This file is no longer used and can be deleted. Old settings below.\r\n\r\n";
            File.WriteAllText(Path.ChangeExtension(legacyPath, ".old"), note + legacyJson);
            File.Delete(legacyPath);
        }
        // e.g. read-only install folder: keep the old file and retry next launch
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void MigrateLegacyIndices()
    {
        SelectedSource ??= LegacyIndexToSelection(SelectedSourceIndex);
        SelectedTarget ??= LegacyIndexToSelection(SelectedTargetIndex);
        SelectedSourceIndex = null;
        SelectedTargetIndex = null;
    }

    private static string? LegacyIndexToSelection(int? index)
    {
        var versions = VersionInfo.BuiltInVersions;
        return index switch
        {
            null or < 0 => null,
            >= LegacyCustomIndex => CustomSelection,
            _ when index < versions.Count => versions[index.Value].FolderName,
            _ => null,
        };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(s_configPath)!);
            File.WriteAllText(s_configPath, JsonSerializer.Serialize(this, s_jsonOptions));
        }
        catch { }
    }
}
