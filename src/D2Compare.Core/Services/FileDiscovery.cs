using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

public static class FileDiscovery
{
    public static FileListResult DiscoverFiles(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath) || !Directory.Exists(targetPath))
            return new FileListResult([], [], []);

        var sourceFiles = Directory.GetFiles(sourcePath)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetFiles = Directory.GetFiles(targetPath)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var common = sourceFiles.Intersect(targetFiles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sourceOnly = sourceFiles.Except(targetFiles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetOnly = targetFiles.Except(sourceFiles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FileListResult(common, sourceOnly, targetOnly);
    }
}