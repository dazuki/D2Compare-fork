namespace D2Compare.Core.Models;

/// <summary>
/// A group of differences for a single row.
/// Key is like "RowName (Row 42)", Changes are individual field diffs.
/// </summary>
public record DiffGroup(string Key, IReadOnlyList<string> Changes);