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

public record UpdateInfo(string TagName, string Version, string DownloadUrl, string ReleaseNotes);

// WorkDir is the private temp folder holding everything; StagingDir has the extracted release
public record StagedUpdate(string WorkDir, string StagingDir);

public class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/dazuki/D2Compare-fork/releases/latest";
    private static readonly HttpClient s_http = new();

    static UpdateService()
    {
        s_http.DefaultRequestHeaders.UserAgent.ParseAdd("D2Compare-Updater/1.0");
        s_http.Timeout = TimeSpan.FromSeconds(30);
    }

    // Set by the AppImage runtime to the path of the running .AppImage file
    private static string? AppImagePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } path && File.Exists(path)
            ? path
            : null;

    // Informational version keeps the "-rc.N" suffix; the SDK appends "+<commit>", which is dropped
    private static string? InformationalVersion =>
        Assembly
            .GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0];

    public static string GetCurrentVersion()
    {
        return InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "0.0.0";
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
            if (current is null)
                return null;

            // A stable release of the same version is newer than its rc builds
            var currentCore = new System.Version(current.Major, current.Minor, current.Build);
            bool isPrerelease = InformationalVersion?.Contains('-') == true;
            if (latest < currentCore || (latest == currentCore && !isPrerelease))
                return null;

            var suffix =
                AppImagePath is not null ? "-x86_64.AppImage"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "-win-x64.zip"
                : "-linux-x64.tar.gz";

            var body = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? ""
                : "";

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    return new UpdateInfo(tag, versionStr, url, body);
                }
            }
        }
        catch
        {
            // Network failure, rate limit, malformed JSON — silently ignore
        }

        return null;
    }

    // Downloads and extracts the update archive into a fresh private temp folder.
    // Returns null on failure.
    public static async Task<StagedUpdate?> DownloadUpdateAsync(
        UpdateInfo info,
        IProgress<double> progress,
        CancellationToken ct = default
    )
    {
        // Random name with owner-only permissions on Unix, so other local users can't plant files
        var workDir = Directory.CreateTempSubdirectory("D2Compare_update_").FullName;
        var stagingDir = Directory.CreateDirectory(Path.Combine(workDir, "staging")).FullName;

        var isAppImage = AppImagePath is not null;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var archivePath =
            isAppImage ? Path.Combine(stagingDir, "D2Compare.AppImage")
            : isWindows ? Path.Combine(workDir, "update.zip")
            : Path.Combine(workDir, "update.tar.gz");

        try
        {
            using var response = await s_http.GetAsync(
                info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(archivePath))
            {
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

            // An AppImage is the whole app in one file; nothing to extract
            if (isAppImage)
                return new StagedUpdate(workDir, stagingDir);

            if (isWindows)
                ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true);
            else
                ExtractTarGz(archivePath, stagingDir);

            File.Delete(archivePath);
            return new StagedUpdate(workDir, stagingDir);
        }
        catch
        {
            TryDeleteDirectory(workDir);
            return null;
        }
    }

    // Launches a detached script that waits for this process to exit, copies the new
    // files over the install, relaunches the app and removes the temp folder.
    // Only files shipped in the release are overwritten; nothing else in the install
    // folder is deleted. Paths are passed as arguments, never pasted into script text.
    public static void LaunchUpdateScriptAndExit(StagedUpdate update)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var pid = Environment.ProcessId.ToString();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        ProcessStartInfo psi;
        if (AppImagePath is { } appImage)
            psi = UnixScriptProcess(
                update.WorkDir,
                AppImageScript,
                pid,
                appImage,
                Path.Combine(update.StagingDir, "D2Compare.AppImage"),
                update.WorkDir
            );
        else if (isWindows)
        {
            var scriptPath = Path.Combine(update.WorkDir, "update.ps1");
            File.WriteAllText(scriptPath, WindowsScript);
            psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (
                var arg in new[]
                {
                    "-ExecutionPolicy",
                    "Bypass",
                    "-NonInteractive",
                    "-WindowStyle",
                    "Hidden",
                    "-File",
                    scriptPath,
                    pid,
                    installDir,
                    update.StagingDir,
                    update.WorkDir,
                    Path.Combine(installDir, "D2Compare.exe"),
                }
            )
                psi.ArgumentList.Add(arg);
        }
        else
            psi = UnixScriptProcess(
                update.WorkDir,
                UnixScript,
                pid,
                installDir,
                update.StagingDir,
                update.WorkDir,
                Path.Combine(installDir, "D2Compare")
            );

        Process.Start(psi);

        if (
            Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
        )
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    // setsid fully detaches the script from this process's terminal session
    private static ProcessStartInfo UnixScriptProcess(
        string workDir,
        string script,
        params string[] args
    )
    {
        var scriptPath = Path.Combine(workDir, "update.sh");
        File.WriteAllText(scriptPath, script);

        var psi = new ProcessStartInfo
        {
            FileName = "setsid",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    // Settings live next to the exe on Windows; never let an archive overwrite them
    private const string WindowsScript = """
        param([int]$TargetPid, [string]$InstallDir, [string]$StagingDir, [string]$WorkDir, [string]$ExePath)
        while (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
        $sep = [System.IO.Path]::DirectorySeparatorChar
        $root = [System.IO.Path]::GetFullPath($StagingDir).TrimEnd($sep) + $sep
        foreach ($file in [System.IO.Directory]::EnumerateFiles($StagingDir, '*', 'AllDirectories')) {
            $relative = $file.Substring($root.Length)
            if ($relative -eq 'D2Compare.settings.json') { continue }
            $dest = [System.IO.Path]::Combine($InstallDir, $relative)
            [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($dest))
            [System.IO.File]::Copy($file, $dest, $true)
        }
        Start-Process -FilePath $ExePath
        Remove-Item -LiteralPath $WorkDir -Recurse -Force
        """;

    private const string UnixScript = """
        #!/bin/bash
        pid="$1"; install_dir="$2"; staging_dir="$3"; work_dir="$4"; exe="$5"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        rm -f -- "$staging_dir/D2Compare.settings.json"
        cp -a -- "$staging_dir/." "$install_dir/"
        chmod +x -- "$exe"
        "$exe" >/dev/null 2>&1 &
        rm -rf -- "$work_dir"
        """;

    // Copies next to the target first so the swap is an atomic rename on the same filesystem.
    // If the copy fails (e.g. read-only folder) the old AppImage is left untouched.
    private const string AppImageScript = """
        #!/bin/bash
        pid="$1"; target="$2"; new_file="$3"; work_dir="$4"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        tmp="$target.update"
        if cp -- "$new_file" "$tmp" && chmod +x -- "$tmp"; then
            mv -f -- "$tmp" "$target"
        else
            rm -f -- "$tmp"
        fi
        "$target" >/dev/null 2>&1 &
        rm -rf -- "$work_dir"
        """;

    private static void ExtractTarGz(string archivePath, string outputDir)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        var outputRoot = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;

        while (tarReader.GetNextEntry() is { } entry)
        {
            var resolvedPath = Path.GetFullPath(Path.Combine(outputDir, entry.Name));
            if (!resolvedPath.StartsWith(outputRoot, StringComparison.Ordinal))
                continue;

            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(resolvedPath);
                continue;
            }

            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                var dir = Path.GetDirectoryName(resolvedPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(resolvedPath, overwrite: true);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
