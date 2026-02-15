using D2Compare.Core.Models;

namespace D2Compare.Core.Services;


public static class ConvertService
{
    // Converts source .txt files toward the target version structure
    public static void ConvertFolder(
        string sourcePath,
        string targetPath,
        string outputPath,
        bool convertColumns,
        RowConversionMode rowMode,
        Action<string>? onProgress = null)
    {
        Directory.CreateDirectory(outputPath);

        var sourceFiles = Directory.GetFiles(sourcePath, "*.txt");
        var targetFiles = Directory.GetFiles(targetPath, "*.txt");

        foreach (var sourceFile in sourceFiles)
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetFile = Array.Find(targetFiles,
                f => Path.GetFileName(f)!.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (targetFile is null) continue;

            onProgress?.Invoke(fileName);

            var sourceData = CsvParser.Parse(sourceFile);
            var targetData = CsvParser.Parse(targetFile);

            var result = CompareService.CompareFile(sourceFile, targetFile, includeNewRows: true);

            var converted = ConvertFile(sourceData, targetData, result, convertColumns, rowMode);
            if (converted is null) continue;

            var outFile = Path.Combine(outputPath, fileName);
            WriteTsv(outFile, converted);
        }
    }

    // Build converted data dictionary (column name -> list of values by row)
    // Returns null if no common row header column (cannot align rows)
    internal static Dictionary<string, List<string>>? ConvertFile(
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        CompareResult result,
        bool convertColumns,
        RowConversionMode rowMode)
    {
        var allHeaders = new HashSet<string>(sourceData.Keys);
        allHeaders.UnionWith(targetData.Keys);
        var rowHeaderColumn = allHeaders.FirstOrDefault(h => sourceData.ContainsKey(h) && targetData.ContainsKey(h));
        if (rowHeaderColumn is null) return null;

        var sourceKeyList = sourceData.Keys.ToList();
        var targetKeyList = targetData.Keys.ToList();

        // Parse renames: "(Col N) old -> new" and "(Row N) old -> new"
        var columnRenameOldToNew = ParseRenames(result.ChangedColumns);
        var columnRenameNewToOld = columnRenameOldToNew.ToDictionary(kv => kv.New, kv => kv.Old);
        var columnRenameOldToNewDict = columnRenameOldToNew.ToDictionary(kv => kv.Old, kv => kv.New);
        var rowRenameOldToNew = ParseRenames(result.ChangedRows);
        var rowRenameNewToOld = rowRenameOldToNew.ToDictionary(kv => kv.New, kv => kv.Old);
        var rowRenameOldToNewDict = rowRenameOldToNew.ToDictionary(kv => kv.Old, kv => kv.New);

        var outputColumns = convertColumns ? targetKeyList : sourceKeyList;
        var sourceRowKeys = sourceData[rowHeaderColumn];
        var targetRowKeys = targetData[rowHeaderColumn];

        var outputRowKeys = BuildOutputRowKeys(rowMode, sourceRowKeys, targetRowKeys,
            rowRenameNewToOld, rowRenameOldToNewDict);

        if (rowMode != RowConversionMode.None)
            outputRowKeys = ApplyVersionRowOrder(outputRowKeys, sourceData, targetData, sourceRowKeys, targetRowKeys, rowHeaderColumn);

        var output = new Dictionary<string, List<string>>();
        foreach (var col in outputColumns)
            output[col] = new List<string>();

        bool rowsFromTarget = rowMode == RowConversionMode.AppendOriginalAtEnd; // None and AppendTarget use source row order

        foreach (var outRowKey in outputRowKeys)
        {
            // When rowsFromTarget (AppendOriginal): resolve to source row; appended source-only rows have no target row
            // When !rowsFromTarget (AppendTarget): resolve to source row; appended target-only rows have no source row (or renamed source row).
            string? sourceRowKey = rowsFromTarget ? (sourceRowKeys.Contains(outRowKey) ? outRowKey : (rowRenameNewToOld.TryGetValue(outRowKey, out var so) ? so : null)) : (sourceRowKeys.Contains(outRowKey) ? outRowKey : (rowRenameNewToOld.TryGetValue(outRowKey, out var oldK) ? oldK : null));
            
            // When rowsFromTarget, only set targetRowKey if target actually has this row (appended source-only rows are not in target)
            string? targetRowKey = rowsFromTarget ? (targetRowKeys.Contains(outRowKey) ? outRowKey : null) : (targetRowKeys.Contains(outRowKey) ? outRowKey : (rowRenameOldToNewDict.TryGetValue(outRowKey, out var tn) ? tn : null));

            int? sourceRowIndex = sourceRowKey is null ? null : sourceRowKeys.IndexOf(sourceRowKey);
            int? targetRowIndex = targetRowKey is null ? null : targetRowKeys.IndexOf(targetRowKey);

            foreach (var outCol in outputColumns)
            {
                string? sourceCol = ResolveColumn(outCol, sourceKeyList, targetKeyList, columnRenameOldToNewDict, columnRenameNewToOld, fromTarget: convertColumns);
                string? targetCol = ResolveColumn(outCol, sourceKeyList, targetKeyList, columnRenameOldToNewDict, columnRenameNewToOld, fromTarget: !convertColumns);

                string value = "";
                if (sourceCol is not null && sourceRowIndex is not null && sourceData[sourceCol].Count > sourceRowIndex.Value)
                    value = sourceData[sourceCol][sourceRowIndex.Value];
                if (string.IsNullOrEmpty(value) && targetCol is not null && targetRowIndex is not null && targetData[targetCol].Count > targetRowIndex.Value)
                    value = targetData[targetCol][targetRowIndex.Value];

                output[outCol].Add(value);
            }
        }

        return output;
    }

    private static List<string> BuildOutputRowKeys(
        RowConversionMode rowMode,
        List<string> sourceRowKeys,
        List<string> targetRowKeys,
        Dictionary<string, string> rowRenameNewToOld,
        Dictionary<string, string> rowRenameOldToNewDict)
    {
        switch (rowMode)
        {
            case RowConversionMode.None:
                return sourceRowKeys;
            case RowConversionMode.AppendOriginalAtEnd:
            {
                var targetSet = new HashSet<string>(targetRowKeys);
                var sourceOnly = sourceRowKeys
                    .Where(s => !targetSet.Contains(s) && !rowRenameOldToNewDict.ContainsKey(s))
                    .ToList();
                return targetRowKeys.Concat(sourceOnly).ToList();
            }
            case RowConversionMode.AppendTargetAtEnd:
            {
                var sourceSet = new HashSet<string>(sourceRowKeys);
                var targetOnly = targetRowKeys
                    .Where(t => !sourceSet.Contains(t) && !rowRenameNewToOld.ContainsKey(t))
                    .ToList();
                return sourceRowKeys.Concat(targetOnly).ToList();
            }
            default:
                return sourceRowKeys;
        }
    }

    // Reorder rows so that: all version "0" first, then row "Expansion", then all version "100", then the rest.

    private static List<string> ApplyVersionRowOrder(
        List<string> rowKeys,
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        List<string> sourceRowKeys,
        List<string> targetRowKeys,
        string rowHeaderColumn)
    {
        string? versionCol = sourceData.Keys.FirstOrDefault(k => string.Equals(k, "version", StringComparison.OrdinalIgnoreCase))
            ?? targetData.Keys.FirstOrDefault(k => string.Equals(k, "version", StringComparison.OrdinalIgnoreCase));
        if (versionCol is null) return rowKeys;

        string GetVersion(string rowKey)
        {
            var srcIdx = sourceRowKeys.IndexOf(rowKey);
            if (srcIdx >= 0 && sourceData.ContainsKey(versionCol) && srcIdx < sourceData[versionCol].Count)
                return sourceData[versionCol][srcIdx].Trim();
            var tgtIdx = targetRowKeys.IndexOf(rowKey);
            if (tgtIdx >= 0 && targetData.ContainsKey(versionCol) && tgtIdx < targetData[versionCol].Count)
                return targetData[versionCol][tgtIdx].Trim();
            return "";
        }

        int GetTier(string rowKey)
        {
            if (string.Equals(rowKey, "Expansion", StringComparison.OrdinalIgnoreCase)) return 1;
            var ver = GetVersion(rowKey);
            if (ver == "0") return 0;
            if (ver == "100") return 2;
            return 3;
        }

        return rowKeys
            .Select((key, index) => (Key: key, Index: index, Tier: GetTier(key)))
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.Index)
            .Select(x => x.Key)
            .ToList();
    }

    private static List<(string Old, string New)> ParseRenames(IReadOnlyList<string> changed)
    {
        var list = new List<(string, string)>();
        foreach (var s in changed)
        {
            var idx = s.IndexOf(") ", StringComparison.Ordinal);
            if (idx < 0) continue;
            var rest = s[(idx + 2)..];
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var oldName = rest[..arrow].Trim();
            var newName = rest[(arrow + 4)..].Trim();
            if (oldName.Length > 0 && newName.Length > 0)
                list.Add((oldName, newName));
        }
        return list;
    }

    // If true, output column came from target (convertColumns=true); resolve to source col
    private static string? ResolveColumn(
        string outputCol,
        List<string> sourceKeyList,
        List<string> targetKeyList,
        Dictionary<string, string> columnRenameOldToNew,
        Dictionary<string, string> columnRenameNewToOld,
        bool fromTarget)
    {
        if (fromTarget)
        {
            if (sourceKeyList.Contains(outputCol)) return outputCol;
            return columnRenameNewToOld.TryGetValue(outputCol, out var oldCol) ? oldCol : null;
        }
        if (targetKeyList.Contains(outputCol)) return outputCol;
        return columnRenameOldToNew.TryGetValue(outputCol, out var newCol) ? newCol : null;
    }

    private static void WriteTsv(string filePath, Dictionary<string, List<string>> data)
    {
        var keys = data.Keys.ToList();
        var rowCount = keys.Count > 0 ? data[keys[0]].Count : 0;

        using var writer = new StreamWriter(filePath);
        writer.WriteLine(string.Join("\t", keys));
        for (int r = 0; r < rowCount; r++)
        {
            var cells = keys.Select(col => data[col][r]);
            writer.WriteLine(string.Join("\t", cells));
        }
    }
}
