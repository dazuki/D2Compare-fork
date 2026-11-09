namespace D2Compare.Core.Models;

public enum ChangeKind
{
    Changed,
    Added,
    Removed,
    Unchanged,
}

// One column/row add, remove or rename. Position is 1-based; null for an unchanged-file placeholder.
public record SchemaDiffRow(
    string FileName,
    ChangeKind Kind,
    int? Position,
    string OldName,
    string NewName
)
{
    public string Change => Kind.ToString();
}

// One cell change. RowLabel names the data row the change belongs to.
public record ValueDiffRow(
    string FileName,
    string RowLabel,
    bool IsNewRow,
    string Column,
    string OldValue,
    string NewValue
);

// Per-file change counts shown in a file's group header. ToString gives the plain name
// so anything that falls back to text (clipboard, automation) still reads the file name.
public record FileSummary(string FileName, int Added, int Removed, int Changed)
{
    public sealed override string ToString() => FileName;
}

// Values panel variant; its own type lets the header show "~changed +new" like the panel header
public record ValueFileSummary(string FileName, int Added, int Changed)
    : FileSummary(FileName, Added, 0, Changed);
