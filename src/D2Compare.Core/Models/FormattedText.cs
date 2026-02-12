namespace D2Compare.Core.Models;

public enum SpanColor
{
    Default,
    Header,
    Added,
    Removed,
    FileName,
    FileNameMuted,
}

public enum SpanStyle
{
    Normal,
    Bold,
}

public record FormattedSpan(string Text, SpanColor Color, SpanStyle Style = SpanStyle.Normal);

public record FormattedLine(IReadOnlyList<FormattedSpan> Spans);

public record FormattedDocument(IReadOnlyList<FormattedLine> Lines)
{
    public static FormattedDocument Empty { get; } = new([]);
}