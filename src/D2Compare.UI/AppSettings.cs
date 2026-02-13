using System.IO;
using System.Text.Json;

namespace D2Compare;

public class AppSettings
{
    private static readonly string s_configPath = Path.Combine(
        AppContext.BaseDirectory, "D2Compare.settings.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    public bool IsDarkMode { get; set; }
    public int SelectedSourceIndex { get; set; } = -1;
    public int SelectedTargetIndex { get; set; } = -1;
    public string? CustomSourcePath { get; set; }
    public string? CustomTargetPath { get; set; }

    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public bool IsMaximized { get; set; }
    public double Column0Star { get; set; } = 1.0;
    public double Column2Star { get; set; } = 1.0;
    public double Column4Star { get; set; } = 1.5;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(s_configPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(s_configPath)) ?? new();
        }
        catch { }

        return new();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(s_configPath, JsonSerializer.Serialize(this, s_jsonOptions));
        }
        catch { }
    }
}