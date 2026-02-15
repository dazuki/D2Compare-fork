namespace D2Compare.Core.Models;

// How to combine source and target rows when converting to target.

public enum RowConversionMode
{
    None,
    AppendOriginalAtEnd,
    AppendTargetAtEnd,
}
