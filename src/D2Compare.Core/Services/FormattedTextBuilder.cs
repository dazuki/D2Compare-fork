using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

// Builds FormattedDocument from CompareResult data, replacing RTF generation.
public static class FormattedTextBuilder
{
    public static FormattedDocument BuildColumnDiffs(CompareResult result, bool isBatchMode) =>
        BuildSchemaDiffs(
            result.FileName,
            "Col",
            result.ChangedColumns,
            result.AddedColumns,
            result.RemovedColumns,
            isBatchMode
        );

    public static FormattedDocument BuildRowDiffs(CompareResult result, bool isBatchMode) =>
        BuildSchemaDiffs(
            result.FileName,
            "Row",
            result.ChangedRows,
            result.AddedRows,
            result.RemovedRows,
            isBatchMode
        );

    private static FormattedDocument BuildSchemaDiffs(
        string fileName,
        string unit,
        IReadOnlyList<Rename> changed,
        IReadOnlyList<IndexedName> added,
        IReadOnlyList<IndexedName> removed,
        bool isBatchMode
    )
    {
        var lines = new List<FormattedLine>();
        var hasChanges = changed.Count > 0 || added.Count > 0 || removed.Count > 0;

        if (isBatchMode)
            lines.Add(hasChanges ? FileNameLine(fileName) : MutedFileNameLine(fileName));

        var pad = isBatchMode ? " " : "";

        foreach (var rename in changed)
            lines.Add(
                Line(
                    Span(
                        $"{pad}Changed ({unit} {rename.Old.Index + 1}): ",
                        SpanColor.Header,
                        SpanStyle.Bold
                    ),
                    Span($"{rename.Old.Name} -> {rename.New.Name}", SpanColor.Default)
                )
            );

        foreach (var entry in added)
            lines.Add(
                Line(
                    Span(
                        $"{pad}Added ({unit} {entry.Index + 1}): ",
                        SpanColor.Added,
                        SpanStyle.Bold
                    ),
                    Span(entry.Name, SpanColor.Default)
                )
            );

        foreach (var entry in removed)
            lines.Add(
                Line(
                    Span(
                        $"{pad}Removed ({unit} {entry.Index + 1}): ",
                        SpanColor.Removed,
                        SpanStyle.Bold
                    ),
                    Span(entry.Name, SpanColor.Default)
                )
            );

        return new FormattedDocument(lines);
    }

    public static FormattedDocument BuildValueDiffs(
        CompareResult result,
        bool isBatchMode,
        bool showOnlyNew = false
    )
    {
        var lines = new List<FormattedLine>();

        var groups = showOnlyNew
            ? result.GroupedDifferences.Where(g => g.IsNew).ToList()
            : result.GroupedDifferences;

        if (isBatchMode)
            lines.Add(
                groups.Count > 0
                    ? FileNameLine(result.FileName)
                    : MutedFileNameLine(result.FileName)
            );

        if (groups.Count == 0)
            return new FormattedDocument(lines);

        var pad = isBatchMode ? " " : "";

        foreach (var group in groups)
        {
            var rowTag = $" (Row {group.Row})";
            if (group.IsNew)
                lines.Add(
                    Line(
                        Span($"{pad}Added{rowTag}: ", SpanColor.Added, SpanStyle.Bold),
                        Span(group.RowName, SpanColor.Header, SpanStyle.Bold)
                    )
                );
            else
                lines.Add(
                    Line(
                        Span($"{pad}{group.RowName}", SpanColor.Header, SpanStyle.Bold),
                        Span(rowTag, SpanColor.Header, SpanStyle.Bold)
                    )
                );

            foreach (var change in group.Changes)
                lines.Add(Line(Span($"{pad} {FormatChange(change)}", SpanColor.Default)));

            lines.Add(Line()); // blank line between groups
        }

        return new FormattedDocument(lines);
    }

    public static FormattedDocument BuildFileDiffs(FileListResult fileList)
    {
        var lines = new List<FormattedLine>();

        foreach (var file in fileList.SourceOnly)
            lines.Add(
                Line(
                    Span("Removed: ", SpanColor.Removed, SpanStyle.Bold),
                    Span(file, SpanColor.Default)
                )
            );

        foreach (var file in fileList.TargetOnly)
            lines.Add(
                Line(
                    Span("Added: ", SpanColor.Added, SpanStyle.Bold),
                    Span(file, SpanColor.Default)
                )
            );

        return new FormattedDocument(lines);
    }

    public static FormattedDocument MergeDocuments(IEnumerable<FormattedDocument> documents)
    {
        var lines = new List<FormattedLine>();
        foreach (var doc in documents)
            lines.AddRange(doc.Lines);
        return new FormattedDocument(lines);
    }

    public static string FormatChange(ValueChange change) =>
        change.OldValue is null
            ? $"{change.Column}: '{change.NewValue}'"
            : $"{change.Column}: '{change.OldValue}' -> '{change.NewValue}'";

    private static FormattedSpan Span(
        string text,
        SpanColor color,
        SpanStyle style = SpanStyle.Normal
    ) => new(text, color, style);

    private static FormattedLine Line(params FormattedSpan[] spans) => new(spans.ToList());

    private static FormattedLine FileNameLine(string fileName) =>
        Line(new FormattedSpan(fileName, SpanColor.FileName, SpanStyle.Bold));

    private static FormattedLine MutedFileNameLine(string fileName) =>
        Line(new FormattedSpan(fileName, SpanColor.FileNameMuted, SpanStyle.Bold));
}
