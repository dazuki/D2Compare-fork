namespace D2Compare.Core.Models;

public record CompareResult(
    string FileName,
    IReadOnlyList<string> AddedColumns,
    IReadOnlyList<string> RemovedColumns,
    IReadOnlyList<string> ChangedColumns,
    IReadOnlyList<string> AddedRows,
    IReadOnlyList<string> RemovedRows,
    IReadOnlyList<string> ChangedRows,
    IReadOnlyList<DiffGroup> GroupedDifferences
);