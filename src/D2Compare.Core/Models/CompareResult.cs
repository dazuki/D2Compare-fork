namespace D2Compare.Core.Models;

// A column or row name and its 0-based position in its file
public record IndexedName(int Index, string Name);

// Old lives in the source file, New in the target file
public record Rename(IndexedName Old, IndexedName New);

public record CompareResult(
    string FileName,
    IReadOnlyList<IndexedName> AddedColumns,
    IReadOnlyList<IndexedName> RemovedColumns,
    IReadOnlyList<Rename> ChangedColumns,
    IReadOnlyList<IndexedName> AddedRows,
    IReadOnlyList<IndexedName> RemovedRows,
    IReadOnlyList<Rename> ChangedRows,
    IReadOnlyList<DiffGroup> GroupedDifferences
)
{
    public static CompareResult Empty(string fileName) => new(fileName, [], [], [], [], [], [], []);

    public bool HasColumnChanges =>
        AddedColumns.Count > 0 || RemovedColumns.Count > 0 || ChangedColumns.Count > 0;

    public bool HasRowChanges =>
        AddedRows.Count > 0 || RemovedRows.Count > 0 || ChangedRows.Count > 0;
}
