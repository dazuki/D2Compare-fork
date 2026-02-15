using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace D2Compare.Services;

public record UpdateInfo(string TagName, string Version, string DownloadUrl);

public class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/dazuki/D2Compare-fork/releases/latest";
    private static readonly HttpClient s_http = new();

    static UpdateService()
    {
        s_http.DefaultRequestHeaders.UserAgent.ParseAdd("D2Compare-Updater/1.0");
        s_http.Timeout = TimeSpan.FromSeconds(30);
    }

    public static string GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    // Checks GitHub for a newer release. Returns null if up-to-date or on failure.
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await s_http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tag.TrimStart('v');
            if (!System.Version.TryParse(versionStr, out var latest))
                return null;

            var current = Assembly.GetEntryAssembly()?.GetName().Version;
            if (current is null || latest <= new System.Version(current.Major, current.Minor, current.Build))
                return null;

            var suffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "-win-x64.zip"
                : "-linux-x64.tar.gz";

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    return new UpdateInfo(tag, versionStr, url);
                }
            }
        }
        catch
        {
            // Network failure, rate limit, malformed JSON — silently ignore
        }

        return null;
    }

    // Downloads and extracts the update archive. Returns staging directory path, or null on failure.
    public static async Task<string?> DownloadUpdateAsync(
        UpdateInfo info,
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "D2Compare_update");
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var archiveExt = isWindows ? ".zip" : ".tar.gz";
        var archivePath = Path.Combine(Path.GetTempPath(), $"D2Compare_update{archiveExt}");

        try
        {
            // Stream download with progress
            using var response = await s_http.GetAsync(
                info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(archivePath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress.Report((double)downloaded / total);
            }
        }
        catch
        {
            CleanupFile(archivePath);
            return null;
        }

        // Extract
        try
        {
            if (isWindows)
            {
                ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true);
            }
            else
            {
                ExtractTarGz(archivePath, stagingDir);
            }

            CleanupFile(archivePath);
        }
        catch
        {
            CleanupFile(archivePath);
            return null;
        }

        return stagingDir;
    }

    // Writes a platform-specific update script, launches it, and exits the app.
    // The script waits for this process to exit, copies new files, relaunches, and cleans up.
    public static void LaunchUpdateScriptAndExit(string stagingDir)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var pid = Environment.ProcessId;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        const string settingsFile = "D2Compare.settings.json";
        var settingsBackup = Path.Combine(Path.GetTempPath(), settingsFile);

        if (isWindows)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "d2compare_update.ps1");
            var exePath = Path.Combine(installDir, "D2Compare.exe");
            File.WriteAllText(scriptPath, $$"""
                $target = {{pid}}
                while (Get-Process -Id $target -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
                # Backup settings
                $settings = "{{installDir}}\{{settingsFile}}"
                $backup = "{{settingsBackup}}"
                if (Test-Path $settings) { Copy-Item $settings $backup -Force }
                # Clean install directory
                Get-ChildItem "{{installDir}}" -Recurse | Remove-Item -Recurse -Force
                # Copy new files
                Copy-Item -Path "{{stagingDir}}\*" -Destination "{{installDir}}" -Recurse -Force
                # Restore settings
                if (Test-Path $backup) { Copy-Item $backup $settings -Force; Remove-Item $backup -Force }
                Start-Process "{{exePath}}"
                Remove-Item -Path "{{stagingDir}}" -Recurse -Force
                Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force
                """);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "d2compare_update.sh");
            var exePath = Path.Combine(installDir, "D2Compare");
            File.WriteAllText(scriptPath, $"""
                #!/bin/bash
                while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
                # Backup settings
                settings="{installDir}/{settingsFile}"
                backup="{settingsBackup}"
                [ -f "$settings" ] && cp "$settings" "$backup"
                # Clean install directory
                rm -rf "{installDir}/"*
                # Copy new files
                cp -rf "{stagingDir}/." "{installDir}/"
                chmod +x "{exePath}"
                # Restore settings
                [ -f "$backup" ] && mv "$backup" "$settings"
                "{exePath}" &
                rm -rf "{stagingDir}"
                rm -- "$0"
                """);
            // Make script executable
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // Use setsid to fully detach from parent terminal session
            Process.Start(new ProcessStartInfo
            {
                FileName = "setsid",
                Arguments = $"bash \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    private static void ExtractTarGz(string archivePath, string outputDir)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        while (tarReader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.Directory)
            {
                var dirPath = Path.Combine(outputDir, entry.Name);
                Directory.CreateDirectory(dirPath);
                continue;
            }

            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                var filePath = Path.Combine(outputDir, entry.Name);
                var dir = Path.GetDirectoryName(filePath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(filePath, overwrite: true);
            }
        }
    }

    private static void CleanupFile(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
