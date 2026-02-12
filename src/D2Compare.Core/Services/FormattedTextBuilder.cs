using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

/// <summary>
/// Builds FormattedDocument from CompareResult data, replacing RTF generation.
/// </summary>
public static class FormattedTextBuilder
{
    public static FormattedDocument BuildColumnDiffs(CompareResult result, bool isBatchMode)
    {
        var lines = new List<FormattedLine>();
        var hasChanges = result.ChangedColumns.Count > 0 || result.AddedColumns.Count > 0 || result.RemovedColumns.Count > 0;

        if (isBatchMode)
            lines.Add(hasChanges ? FileNameLine(result.FileName) : MutedFileNameLine(result.FileName));

        var pad = isBatchMode ? " " : "";

        foreach (var changed in result.ChangedColumns)
            lines.Add(Line(Span(pad + "Changed: ", SpanColor.Header, SpanStyle.Bold), Span(changed, SpanColor.Default)));

        foreach (var added in result.AddedColumns)
            lines.Add(Line(Span(pad + "Added: ", SpanColor.Added, SpanStyle.Bold), Span(added, SpanColor.Default)));

        foreach (var removed in result.RemovedColumns)
            lines.Add(Line(Span(pad + "Removed: ", SpanColor.Removed, SpanStyle.Bold), Span(removed, SpanColor.Default)));

        return new FormattedDocument(lines);
    }

    public static FormattedDocument BuildRowDiffs(CompareResult result, bool isBatchMode)
    {
        var lines = new List<FormattedLine>();
        var hasChanges = result.ChangedRows.Count > 0 || result.AddedRows.Count > 0 || result.RemovedRows.Count > 0;

        if (isBatchMode)
            lines.Add(hasChanges ? FileNameLine(result.FileName) : MutedFileNameLine(result.FileName));

        var pad = isBatchMode ? " " : "";

        foreach (var changed in result.ChangedRows)
            lines.Add(Line(Span(pad + "Changed: ", SpanColor.Header, SpanStyle.Bold), Span(changed, SpanColor.Default)));

        foreach (var added in result.AddedRows)
            lines.Add(Line(Span(pad + "Added: ", SpanColor.Added, SpanStyle.Bold), Span(added, SpanColor.Default)));

        foreach (var removed in result.RemovedRows)
            lines.Add(Line(Span(pad + "Removed: ", SpanColor.Removed, SpanStyle.Bold), Span(removed, SpanColor.Default)));

        return new FormattedDocument(lines);
    }

    public static FormattedDocument BuildValueDiffs(CompareResult result, bool isBatchMode)
    {
        var lines = new List<FormattedLine>();

        if (isBatchMode)
            lines.Add(result.GroupedDifferences.Count > 0 ? FileNameLine(result.FileName) : MutedFileNameLine(result.FileName));

        if (result.GroupedDifferences.Count == 0)
            return new FormattedDocument(lines);

        var pad = isBatchMode ? " " : "";

        foreach (var group in result.GroupedDifferences)
        {
            lines.Add(Line(Span(pad + group.Key, SpanColor.Header, SpanStyle.Bold)));

            foreach (var diff in group.Changes)
            {
                var spans = ParseBoldMarkup(pad + "- " + diff);
                lines.Add(new FormattedLine(spans));
            }

            lines.Add(Line()); // blank line between groups
        }

        return new FormattedDocument(lines);
    }

    public static FormattedDocument BuildFileDiffs(FileListResult fileList)
    {
        var lines = new List<FormattedLine>();

        foreach (var file in fileList.SourceOnly)
            lines.Add(Line(Span("Removed: ", SpanColor.Removed, SpanStyle.Bold), Span(file, SpanColor.Default)));

        foreach (var file in fileList.TargetOnly)
            lines.Add(Line(Span("Added: ", SpanColor.Added, SpanStyle.Bold), Span(file, SpanColor.Default)));

        return new FormattedDocument(lines);
    }

    public static FormattedDocument MergeDocuments(IEnumerable<FormattedDocument> documents)
    {
        var lines = new List<FormattedLine>();
        foreach (var doc in documents)
            lines.AddRange(doc.Lines);
        return new FormattedDocument(lines);
    }

    private static List<FormattedSpan> ParseBoldMarkup(string text)
    {
        var spans = new List<FormattedSpan>();
        int pos = 0;

        while (pos < text.Length)
        {
            int boldStart = text.IndexOf("<b>", pos, StringComparison.Ordinal);
            if (boldStart == -1)
            {
                if (pos < text.Length)
                    spans.Add(Span(text[pos..], SpanColor.Default));
                break;
            }

            if (boldStart > pos)
                spans.Add(Span(text[pos..boldStart], SpanColor.Default));

            int boldEnd = text.IndexOf("</b>", boldStart, StringComparison.Ordinal);
            if (boldEnd == -1)
            {
                spans.Add(Span(text[(boldStart + 3)..], SpanColor.Default, SpanStyle.Bold));
                break;
            }

            spans.Add(Span(text[(boldStart + 3)..boldEnd], SpanColor.Default, SpanStyle.Bold));
            pos = boldEnd + 4;
        }

        return spans;
    }

    private static FormattedSpan Span(string text, SpanColor color, SpanStyle style = SpanStyle.Normal) =>
        new(text, color, style);

    private static FormattedLine Line(params FormattedSpan[] spans) =>
        new(spans.ToList());

    private static FormattedLine FileNameLine(string fileName) =>
        Line(new FormattedSpan(fileName, SpanColor.FileName, SpanStyle.Bold));

    private static FormattedLine MutedFileNameLine(string fileName) =>
        Line(new FormattedSpan(fileName, SpanColor.FileNameMuted, SpanStyle.Bold));
}