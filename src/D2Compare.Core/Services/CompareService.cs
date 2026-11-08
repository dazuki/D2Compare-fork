using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

public static class CompareService
{
    public static CompareResult CompareFile(
        string sourcePath,
        string targetPath,
        bool includeNewRows
    ) =>
        Compare(
            Path.GetFileName(sourcePath),
            CsvParser.Parse(sourcePath),
            CsvParser.Parse(targetPath),
            includeNewRows
        );

    public static CompareResult Compare(
        string fileName,
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        bool includeNewRows
    )
    {
        var rowHeaderColumn = FindRowHeaderColumn(sourceData, targetData);
        if (rowHeaderColumn is null)
            return CompareResult.Empty(fileName);

        var schema = AnalyzeSchema(fileName, sourceData, targetData);
        var groups = DiffEngine.GetGroupedDifferences(
            sourceData,
            targetData,
            rowHeaderColumn,
            includeNewRows
        );
        return schema with { GroupedDifferences = groups };
    }

    // Column and row diffs only, skipping the expensive value diffs
    public static CompareResult AnalyzeSchema(
        string fileName,
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData
    )
    {
        var rowHeaderColumn = FindRowHeaderColumn(sourceData, targetData);
        if (rowHeaderColumn is null)
            return CompareResult.Empty(fileName);

        var removedColumns = Indexed(sourceData.Keys)
            .Where(c => !targetData.ContainsKey(c.Name))
            .ToList();
        var addedColumns = Indexed(targetData.Keys)
            .Where(c => !sourceData.ContainsKey(c.Name))
            .ToList();
        var columns = MatchRenames(fileName, removedColumns, addedColumns);

        var sourceRowKeys = RowInstanceKeyHelper.BuildKeys(sourceData[rowHeaderColumn]);
        var targetRowKeys = RowInstanceKeyHelper.BuildKeys(targetData[rowHeaderColumn]);
        var rows = MatchRenames(
            fileName,
            Missing(sourceRowKeys, targetRowKeys.ToHashSet()),
            Missing(targetRowKeys, sourceRowKeys.ToHashSet())
        );

        return new CompareResult(
            fileName,
            columns.Added,
            columns.Removed,
            columns.Renames,
            rows.Added,
            rows.Removed,
            rows.Renames,
            []
        );
    }

    // First source column that also exists in the target; D2 files key rows by their first column
    public static string? FindRowHeaderColumn(
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData
    ) => sourceData.Keys.FirstOrDefault(targetData.ContainsKey);

    public static List<CompareResult> CompareFolder(
        string sourcePath,
        string targetPath,
        bool includeNewRows,
        Action<string>? onProgress = null
    )
    {
        var results = new List<CompareResult>();

        foreach (var (sourceFile, targetFile) in MatchTxtFiles(sourcePath, targetPath))
        {
            onProgress?.Invoke(Path.GetFileName(sourceFile));
            results.Add(CompareFile(sourceFile, targetFile, includeNewRows));
        }

        return results;
    }

    // Pairs .txt files present in both folders. Directory.GetFiles glob is case-sensitive
    // on Linux and D2R files may be extracted as uppercase .TXT, so match case-insensitively.
    public static IEnumerable<(string Source, string Target)> MatchTxtFiles(
        string sourcePath,
        string targetPath
    )
    {
        var targetByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetFile in GetTxtFiles(targetPath))
            targetByName.TryAdd(Path.GetFileName(targetFile), targetFile);

        foreach (var sourceFile in GetTxtFiles(sourcePath))
        {
            if (targetByName.TryGetValue(Path.GetFileName(sourceFile), out var targetFile))
                yield return (sourceFile, targetFile);
        }
    }

    // Pairs each removed entry with at most one added entry using the known rename rules
    internal static (
        List<Rename> Renames,
        List<IndexedName> Removed,
        List<IndexedName> Added
    ) MatchRenames(string fileName, List<IndexedName> removed, List<IndexedName> added)
    {
        var addedByKey = new Dictionary<string, Queue<int>>();
        var addedByIndex = new Dictionary<int, int>();
        for (int i = 0; i < added.Count; i++)
        {
            var key = SchemaFixProvider.Canonical(added[i].Name);
            if (!addedByKey.TryGetValue(key, out var queue))
                addedByKey[key] = queue = new Queue<int>();
            queue.Enqueue(i);
            addedByIndex[added[i].Index] = i;
        }

        var used = new bool[added.Count];
        var renames = new List<Rename>();
        var remainingRemoved = new List<IndexedName>();

        foreach (var oldEntry in removed)
        {
            var match = FindByName(fileName, oldEntry, addedByKey, used);
            if (
                match < 0
                && addedByIndex.TryGetValue(oldEntry.Index, out var slot)
                && !used[slot]
                && SchemaFixProvider.IsReusedSlot(fileName, oldEntry, added[slot])
            )
                match = slot;

            if (match < 0)
            {
                remainingRemoved.Add(oldEntry);
                continue;
            }

            used[match] = true;
            renames.Add(new Rename(oldEntry, added[match]));
        }

        var remainingAdded = added.Where((_, i) => !used[i]).ToList();
        return (renames, remainingRemoved, remainingAdded);
    }

    private static int FindByName(
        string fileName,
        IndexedName oldEntry,
        Dictionary<string, Queue<int>> addedByKey,
        bool[] used
    )
    {
        foreach (var key in SchemaFixProvider.RenameKeys(fileName, oldEntry.Name))
        {
            if (!addedByKey.TryGetValue(key, out var queue))
                continue;
            while (queue.Count > 0 && used[queue.Peek()])
                queue.Dequeue();
            if (queue.Count > 0)
                return queue.Dequeue();
        }
        return -1;
    }

    private static IEnumerable<IndexedName> Indexed(IEnumerable<string> names) =>
        names.Select((name, i) => new IndexedName(i, name));

    // Row instances in `rows` that are absent from `other`
    private static List<IndexedName> Missing(
        List<RowInstanceKey> rows,
        HashSet<RowInstanceKey> other
    ) =>
        rows.Select((key, i) => (key, i))
            .Where(x => !other.Contains(x.key))
            .Select(x => new IndexedName(x.i, x.key.Name))
            .ToList();

    private static string[] GetTxtFiles(string path) =>
        Directory
            .GetFiles(path)
            .Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
