using System.Text;

namespace D2Compare.Core.Tests;

// Temp folders with D2-style TSV files, plus access to the bundled TXT data
public sealed class TestFiles : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("D2CompareTests_").FullName;

    public string Dir(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

    // Rows are tab-joined and written with CRLF like real D2 files
    public string Write(string dir, string fileName, params string[][] rows) =>
        WriteRaw(
            dir,
            fileName,
            Encoding.UTF8.GetBytes(
                string.Join("\r\n", rows.Select(r => string.Join("\t", r))) + "\r\n"
            )
        );

    public string WriteRaw(string dir, string fileName, byte[] bytes)
    {
        var path = Path.Combine(Dir(dir), fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string BundledTxt(string version, string fileName)
    {
        var dir = Path.Combine(RepoRoot(), "TXT", version);
        return Directory
            .GetFiles(dir)
            .First(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "D2Compare.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
