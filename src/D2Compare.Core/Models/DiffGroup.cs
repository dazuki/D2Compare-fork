namespace D2Compare.Core.Models;

// OldValue is null for cells of a row that only exists in the target
public record ValueChange(string Column, string? OldValue, string NewValue);

// All value changes for one row. Row is the 1-based row number shown to the user.
public record DiffGroup(
    int Row,
    string RowName,
    IReadOnlyList<ValueChange> Changes,
    bool IsNew = false
);
