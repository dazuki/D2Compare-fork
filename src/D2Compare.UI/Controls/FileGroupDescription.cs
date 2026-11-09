using System.Globalization;
using Avalonia.Collections;
using D2Compare.Core.Models;

namespace D2Compare.Controls;

// Groups rows by file with a FileSummary key, so the group header can show per-file counts
public sealed class FileGroupDescription(IReadOnlyDictionary<string, FileSummary> summaries)
    : DataGridGroupDescription
{
    public override object GroupKeyFromItem(object item, int level, CultureInfo culture)
    {
        var fileName = item switch
        {
            SchemaDiffRow row => row.FileName,
            ValueDiffRow row => row.FileName,
            _ => "",
        };
        return summaries.TryGetValue(fileName, out var summary)
            ? summary
            : new FileSummary(fileName, 0, 0, 0);
    }
}
