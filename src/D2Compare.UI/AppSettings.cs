using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Compare.Core.Models;

namespace D2Compare;

public class AppSettings
{
    private static readonly string s_configPath = RuntimeInformation.IsOSPlatform(
        OSPlatform.Windows
    )
        ? Path.Combine(AppContext.BaseDirectory, "D2Compare.settings.json")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "D2Compare",
            "settings.json"
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
