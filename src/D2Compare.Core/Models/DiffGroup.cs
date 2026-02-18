namespace D2Compare.Core.Models;

// A group of differences for a single row.
// Key is like "(Row 42) RowName", Changes are individual field diffs.
public record DiffGroup(string Key, IReadOnlyList<string> Changes, bool IsNew = false);