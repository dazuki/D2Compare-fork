using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

// Flattens CompareResult data into table rows for the grid panels.
public static class DiffRowBuilder
{
    public static List<SchemaDiffRow> BuildColumnRows(
        IEnumerable<CompareResult> results,
        bool includeUnchanged
    ) =>
        results
            .SelectMany(r =>
                BuildSchemaRows(
                    r.FileName,
                    r.ChangedColumns,
                    r.AddedColumns,
                    r.RemovedColumns,
                    includeUnchanged
                )
            )
            .ToList();

    public static List<SchemaDiffRow> BuildRowRows(
        IEnumerable<CompareResult> results,
        bool includeUnchanged
    ) =>
        results
            .SelectMany(r =>
                BuildSchemaRows(
                    r.FileName,
                    r.ChangedRows,
                    r.AddedRows,
                    r.RemovedRows,
                    includeUnchanged
                )
            )
            .ToList();

    // Files without value changes get no rows: an empty values group says nothing useful
    public static List<ValueDiffRow> BuildValueRows(
        IEnumerable<CompareResult> results,
        bool showOnlyNew
    )
    {
        var rows = new List<ValueDiffRow>();
        foreach (var result in results)
        foreach (var group in result.GroupedDifferences)
        {
            if (showOnlyNew && !group.IsNew)
                continue;

            var label = RowLabel(group);
            foreach (var change in group.Changes)
                rows.Add(
                    new ValueDiffRow(
                        result.FileName,
                        label,
                        group.IsNew,
                        change.Column,
                        change.OldValue ?? "",
                        change.NewValue
                    )
                );
        }
        return rows;
    }

    public static string RowLabel(DiffGroup group) =>
        group.IsNew
            ? $"Added (Row {group.Row}): {group.RowName}"
            : $"{group.RowName} (Row {group.Row})";

    // Unchanged placeholders carry no data, so they never match a search
    public static List<SchemaDiffRow> Filter(IEnumerable<SchemaDiffRow> rows, string term) =>
        rows.Where(r =>
                r.Kind != ChangeKind.Unchanged
                && (
                    Matches(r.Change, term)
                    || Matches(r.Position?.ToString(), term)
                    || Matches(r.OldName, term)
                    || Matches(r.NewName, term)
                )
            )
            .ToList();

    // A RowLabel match keeps every change of that data row, so the context stays visible
    public static List<ValueDiffRow> Filter(IEnumerable<ValueDiffRow> rows, string term) =>
        rows.Where(r =>
                Matches(r.RowLabel, term)
                || Matches(r.Column, term)
                || Matches(r.OldValue, term)
                || Matches(r.NewValue, term)
            )
            .ToList();

    public static Dictionary<string, FileSummary> Summarize(IEnumerable<SchemaDiffRow> rows) =>
        rows.GroupBy(r => r.FileName)
            .ToDictionary(
                g => g.Key,
                g => new FileSummary(
                    g.Key,
                    g.Count(r => r.Kind == ChangeKind.Added),
                    g.Count(r => r.Kind == ChangeKind.Removed),
                    g.Count(r => r.Kind == ChangeKind.Changed)
                )
            );

    // Counts data rows (not cells), matching the Values panel header
    public static Dictionary<string, FileSummary> Summarize(IEnumerable<ValueDiffRow> rows) =>
        rows.GroupBy(r => r.FileName)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var labels = g.DistinctBy(r => r.RowLabel).ToList();
                    return (FileSummary)
                        new ValueFileSummary(
                            g.Key,
                            labels.Count(r => r.IsNewRow),
                            labels.Count(r => !r.IsNewRow)
                        );
                }
            );

    private static bool Matches(string? text, string term) =>
        text is not null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    // Added first, then Removed, then Changed, so new content leads each file
    private static IEnumerable<SchemaDiffRow> BuildSchemaRows(
        string fileName,
        IReadOnlyList<Rename> changed,
        IReadOnlyList<IndexedName> added,
        IReadOnlyList<IndexedName> removed,
        bool includeUnchanged
    )
    {
        if (changed.Count == 0 && added.Count == 0 && removed.Count == 0)
        {
            if (includeUnchanged)
                yield return new SchemaDiffRow(fileName, ChangeKind.Unchanged, null, "", "");
            yield break;
        }

        foreach (var entry in added)
            yield return new SchemaDiffRow(
                fileName,
                ChangeKind.Added,
                entry.Index + 1,
                "",
                entry.Name
            );

        foreach (var entry in removed)
            yield return new SchemaDiffRow(
                fileName,
                ChangeKind.Removed,
                entry.Index + 1,
                entry.Name,
                ""
            );

        foreach (var rename in changed)
            yield return new SchemaDiffRow(
                fileName,
                ChangeKind.Changed,
                rename.Old.Index + 1,
                rename.Old.Name,
                rename.New.Name
            );
    }
}
