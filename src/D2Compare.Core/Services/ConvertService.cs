using System.Text;
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
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(outputPath);

        foreach (
            var (sourceFile, targetFile) in CompareService.MatchTxtFiles(sourcePath, targetPath)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(sourceFile);
            onProgress?.Invoke(fileName);

            var sourceData = CsvParser.Parse(sourceFile, out var encoding);
            var targetData = CsvParser.Parse(targetFile);
            var schema = CompareService.AnalyzeSchema(fileName, sourceData, targetData);

            var converted = ConvertFile(sourceData, targetData, schema, convertColumns, rowMode);
            if (converted is null)
                continue;

            WriteTsv(Path.Combine(outputPath, fileName), converted, encoding);
        }
    }

    // Build converted data (column name -> values by row).
    // Returns null if there is no common row header column to align rows on.
    internal static Dictionary<string, List<string>>? ConvertFile(
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        CompareResult schema,
        bool convertColumns,
        RowConversionMode rowMode
    )
    {
        var rowHeaderColumn = CompareService.FindRowHeaderColumn(sourceData, targetData);
        if (rowHeaderColumn is null)
            return null;

        var sourceRowNames = sourceData[rowHeaderColumn];
        var targetRowNames = targetData[rowHeaderColumn];

        var rows = BuildOutputRows(rowMode, sourceRowNames, targetRowNames, schema.ChangedRows);
        if (rowMode != RowConversionMode.None)
            rows = ApplyVersionRowOrder(
                rows,
                sourceData,
                targetData,
                sourceRowNames,
                targetRowNames
            );

        var outputColumns = convertColumns ? targetData.Keys : sourceData.Keys;
        var oldToNew = new Dictionary<string, string>();
        var newToOld = new Dictionary<string, string>();
        foreach (var rename in schema.ChangedColumns)
        {
            oldToNew.TryAdd(rename.Old.Name, rename.New.Name);
            newToOld.TryAdd(rename.New.Name, rename.Old.Name);
        }

        var output = new Dictionary<string, List<string>>();
        foreach (var outCol in outputColumns)
        {
            var sourceColumn = ResolveColumn(outCol, sourceData, newToOld);
            var targetColumn = ResolveColumn(outCol, targetData, oldToNew);

            var values = new List<string>(rows.Count);
            foreach (var (sourceRow, targetRow) in rows)
            {
                var value = sourceColumn is not null && sourceRow is int s ? sourceColumn[s] : "";
                if (value.Length == 0 && targetColumn is not null && targetRow is int t)
                    value = targetColumn[t];
                values.Add(value);
            }
            output[outCol] = values;
        }

        return output;
    }

    // Each output row points at its source and/or target row by position, so rows that
    // share a name (common in D2 files) never get mixed up.
    private static List<(int? Source, int? Target)> BuildOutputRows(
        RowConversionMode rowMode,
        List<string> sourceRowNames,
        List<string> targetRowNames,
        IReadOnlyList<Rename> rowRenames
    )
    {
        var sourceKeys = RowInstanceKeyHelper.BuildKeys(sourceRowNames);
        var targetKeys = RowInstanceKeyHelper.BuildKeys(targetRowNames);
        var sourceByKey = RowInstanceKeyHelper.IndexByKey(sourceKeys);
        var targetByKey = RowInstanceKeyHelper.IndexByKey(targetKeys);

        var renameToTarget = new Dictionary<int, int>();
        var renameToSource = new Dictionary<int, int>();
        foreach (var rename in rowRenames)
        {
            renameToTarget[rename.Old.Index] = rename.New.Index;
            renameToSource[rename.New.Index] = rename.Old.Index;
        }

        int? TargetFor(int s) =>
            targetByKey.TryGetValue(sourceKeys[s], out var t) ? t
            : renameToTarget.TryGetValue(s, out var renamed) ? renamed
            : null;

        int? SourceFor(int t) =>
            sourceByKey.TryGetValue(targetKeys[t], out var s) ? s
            : renameToSource.TryGetValue(t, out var renamed) ? renamed
            : null;

        var sourceRows = Enumerable.Range(0, sourceKeys.Count);
        var targetRows = Enumerable.Range(0, targetKeys.Count);

        return rowMode switch
        {
            RowConversionMode.AppendOriginalAtEnd => targetRows
                .Select(t => (SourceFor(t), (int?)t))
                .Concat(
                    sourceRows.Where(s => TargetFor(s) is null).Select(s => ((int?)s, (int?)null))
                )
                .ToList(),
            RowConversionMode.AppendTargetAtEnd => sourceRows
                .Select(s => ((int?)s, TargetFor(s)))
                .Concat(
                    targetRows.Where(t => SourceFor(t) is null).Select(t => ((int?)null, (int?)t))
                )
                .ToList(),
            _ => sourceRows.Select(s => ((int?)s, TargetFor(s))).ToList(),
        };
    }

    // Reorder rows so that: all version "0" first, then row "Expansion", then all version "100", then the rest.
    private static List<(int? Source, int? Target)> ApplyVersionRowOrder(
        List<(int? Source, int? Target)> rows,
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        List<string> sourceRowNames,
        List<string> targetRowNames
    )
    {
        var sourceVersion = FindVersionColumn(sourceData);
        var targetVersion = FindVersionColumn(targetData);
        if (sourceVersion is null && targetVersion is null)
            return rows;

        string Version((int? Source, int? Target) row)
        {
            if (row.Source is int s && sourceVersion is not null)
                return sourceVersion[s].Trim();
            if (row.Target is int t && targetVersion is not null)
                return targetVersion[t].Trim();
            return "";
        }

        int Tier((int? Source, int? Target) row)
        {
            var name = row.Source is int s ? sourceRowNames[s] : targetRowNames[row.Target!.Value];
            if (string.Equals(name, "Expansion", StringComparison.OrdinalIgnoreCase))
                return 1;
            return Version(row) switch
            {
                "0" => 0,
                "100" => 2,
                _ => 3,
            };
        }

        return rows.Select((row, index) => (row, index, tier: Tier(row)))
            .OrderBy(x => x.tier)
            .ThenBy(x => x.index)
            .Select(x => x.row)
            .ToList();
    }

    private static List<string>? FindVersionColumn(Dictionary<string, List<string>> data) =>
        data.FirstOrDefault(kv =>
            string.Equals(kv.Key, "version", StringComparison.OrdinalIgnoreCase)
        ).Value;

    // The column holding `outputCol` in `data`, following a rename if it has another name there
    private static List<string>? ResolveColumn(
        string outputCol,
        Dictionary<string, List<string>> data,
        Dictionary<string, string> renames
    )
    {
        if (data.TryGetValue(outputCol, out var column))
            return column;
        return renames.TryGetValue(outputCol, out var renamed)
            ? data.GetValueOrDefault(renamed)
            : null;
    }

    // D2 TXT files require CRLF line endings; encoding mirrors the source file
    // (Windows-1252 for Legacy, UTF-8 for D2R) so values round-trip exactly.
    private static void WriteTsv(
        string filePath,
        Dictionary<string, List<string>> data,
        Encoding encoding
    )
    {
        var keys = data.Keys.ToList();
        var rowCount = keys.Count > 0 ? data[keys[0]].Count : 0;

        using var writer = new StreamWriter(filePath, false, encoding) { NewLine = "\r\n" };
        writer.WriteLine(string.Join("\t", keys));
        for (int r = 0; r < rowCount; r++)
        {
            var cells = keys.Select(col => data[col][r]);
            writer.WriteLine(string.Join("\t", cells));
        }
    }
}
