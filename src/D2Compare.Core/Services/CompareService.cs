using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

public static class CompareService
{
    public static CompareResult CompareFile(string sourcePath, string targetPath, bool includeNewRows)
    {
        var sourceData = CsvParser.Parse(sourcePath);
        var targetData = CsvParser.Parse(targetPath);

        var allHeaders = new HashSet<string>(sourceData.Keys);
        allHeaders.UnionWith(targetData.Keys);

        var rowHeaderColumn = allHeaders.FirstOrDefault(h => sourceData.ContainsKey(h) && targetData.ContainsKey(h));
        if (rowHeaderColumn is null)
        {
            return new CompareResult(
                Path.GetFileName(sourcePath), [], [], [], [], [], [],
                []);
        }

        // Column diffs
        var addedColumns = targetData.Keys.Except(sourceData.Keys).ToList();
        var removedColumns = sourceData.Keys.Except(targetData.Keys).ToList();

        // Identify renames via manual fixes
        var changedColumns = new List<string>();
        var remainingAdded = new List<string>(addedColumns);
        var remainingRemoved = new List<string>(removedColumns);

        foreach (var added in addedColumns.ToList())
        {
            foreach (var removed in removedColumns.ToList())
            {
                if (SchemaFixProvider.IsKnownRename(added, removed, sourcePath))
                {
                    changedColumns.Add($"{removed} -> {added}");
                    remainingAdded.Remove(added);
                    remainingRemoved.Remove(removed);
                    break;
                }
            }
        }

        // If equal remaining counts, treat as renames
        if (remainingAdded.Count == remainingRemoved.Count && remainingAdded.Count > 0)
        {
            var paired = remainingAdded.Zip(remainingRemoved, (a, r) => $"{r} -> {a}").ToList();
            changedColumns.AddRange(paired);
            remainingAdded.Clear();
            remainingRemoved.Clear();
        }

        // Row diffs
        var addedRowsTask = Task.Run(() => targetData[rowHeaderColumn].Except(sourceData[rowHeaderColumn]).ToList());
        var removedRowsDict = DiffEngine.GetRemovedRows(sourceData, targetData, rowHeaderColumn);
        var allRemovedRows = DiffEngine.ExpandCounts(removedRowsDict);
        var addedRows = addedRowsTask.Result;

        // Identify row renames
        var changedRows = new List<string>();
        var processedAdded = new HashSet<string>();
        var processedRemoved = new HashSet<string>();

        foreach (var added in addedRows)
        {
            foreach (var removed in allRemovedRows)
            {
                if (!processedAdded.Contains(added) && !processedRemoved.Contains(removed) &&
                    SchemaFixProvider.IsKnownRename(added, removed, sourcePath))
                {
                    changedRows.Add($"{removed} -> {added}");
                    processedAdded.Add(added);
                    processedRemoved.Add(removed);
                }
            }
        }

        if (addedRows.Count == allRemovedRows.Count)
        {
            var paired = addedRows.Zip(allRemovedRows, (a, r) => $"{r} -> {a}");
            foreach (var pair in paired)
            {
                processedAdded.Add(pair.Split(" -> ")[1]);
                processedRemoved.Add(pair.Split(" -> ")[0]);
            }
            changedRows.AddRange(paired);
        }

        var finalAddedRows = addedRows.Where(r => !processedAdded.Contains(r)).ToList();
        var finalRemovedRows = allRemovedRows.Where(r => !processedRemoved.Contains(r)).ToList();

        // Value-level diffs
        var groupedDifferences = DiffEngine.GetGroupedDifferences(
            sourceData, targetData, allHeaders, rowHeaderColumn, includeNewRows);

        return new CompareResult(
            Path.GetFileName(sourcePath),
            remainingAdded,
            remainingRemoved,
            changedColumns,
            finalAddedRows,
            finalRemovedRows,
            changedRows,
            groupedDifferences);
    }

    public static List<CompareResult> CompareFolder(
        string sourcePath, string targetPath, bool includeNewRows,
        Action<string>? onProgress = null)
    {
        var results = new List<CompareResult>();
        var sourceFiles = Directory.GetFiles(sourcePath, "*.txt");
        var targetFiles = Directory.GetFiles(targetPath, "*.txt");

        foreach (var sourceFile in sourceFiles)
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetFile = Array.Find(targetFiles,
                f => Path.GetFileName(f)!.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (targetFile is not null)
            {
                onProgress?.Invoke(fileName);
                results.Add(CompareFile(sourceFile, targetFile, includeNewRows));
            }
        }

        return results;
    }
}