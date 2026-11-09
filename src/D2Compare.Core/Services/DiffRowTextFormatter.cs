using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

// Plain-text export of the grid rows, so Export Summary writes exactly what a panel shows.
public static class DiffRowTextFormatter
{
    // unit is "Col" or "Row"; batch output starts each file with its name
    public static IEnumerable<string> FormatSchemaRows(
        IEnumerable<SchemaDiffRow> rows,
        string unit,
        bool isBatchMode
    )
    {
        var pad = isBatchMode ? " " : "";
        string? currentFile = null;

        foreach (var row in rows)
        {
            if (isBatchMode && row.FileName != currentFile)
                yield return row.FileName;
            currentFile = row.FileName;

            var name = row.Kind switch
            {
                ChangeKind.Changed => $"{row.OldName} -> {row.NewName}",
                ChangeKind.Added => row.NewName,
                ChangeKind.Removed => row.OldName,
                _ => null,
            };
            if (name is not null)
                yield return $"{pad}{row.Kind} ({unit} {row.Position}): {name}";
        }
    }

    public static IEnumerable<string> FormatValueRows(
        IEnumerable<ValueDiffRow> rows,
        bool isBatchMode
    )
    {
        var pad = isBatchMode ? " " : "";
        string? currentFile = null;
        string? currentLabel = null;

        foreach (var row in rows)
        {
            var newFile = row.FileName != currentFile;
            if (currentLabel is not null && (newFile || row.RowLabel != currentLabel))
                yield return ""; // blank line between data rows

            if (isBatchMode && newFile)
                yield return row.FileName;

            if (newFile || row.RowLabel != currentLabel)
                yield return $"{pad}{row.RowLabel}";

            currentFile = row.FileName;
            currentLabel = row.RowLabel;

            yield return row.IsNewRow
                ? $"{pad} {row.Column}: '{row.NewValue}'"
                : $"{pad} {row.Column}: '{row.OldValue}' -> '{row.NewValue}'";
        }

        if (currentLabel is not null)
            yield return "";
    }
}
