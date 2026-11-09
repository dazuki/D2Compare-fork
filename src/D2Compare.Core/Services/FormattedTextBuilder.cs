using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

// Builds the File Summary document (files only in source or target).
public static class FormattedTextBuilder
{
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

    private static FormattedSpan Span(
        string text,
        SpanColor color,
        SpanStyle style = SpanStyle.Normal
    ) => new(text, color, style);

    private static FormattedLine Line(params FormattedSpan[] spans) => new(spans.ToList());
}
