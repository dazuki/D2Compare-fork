namespace D2Compare.Core.Models;

/// <summary>
/// A group of differences for a single row.
/// Key is like "[r42] RowName", Changes are individual field diffs.
/// </summary>
public record DiffGroup(string Key, IReadOnlyList<string> Changes, bool IsNew = false);