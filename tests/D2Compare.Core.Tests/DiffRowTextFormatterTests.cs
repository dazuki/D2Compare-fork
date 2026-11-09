using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class DiffRowTextFormatterTests
{
    [Test]
    public void FormatSchemaRows_WritesOneLinePerChange()
    {
        SchemaDiffRow[] rows =
        [
            new("Armor.txt", ChangeKind.Added, 5, "", "reqdex"),
            new("Armor.txt", ChangeKind.Changed, 2, "HraltiMin", "HratliMin"),
        ];

        var lines = DiffRowTextFormatter.FormatSchemaRows(rows, "Col", isBatchMode: false);

        Assert.That(
            lines,
            Is.EqualTo(new[] { "Added (Col 5): reqdex", "Changed (Col 2): HraltiMin -> HratliMin" })
        );
    }

    [Test]
    public void FormatSchemaRows_WritesFileNameOnly_ForUnchangedFileInBatch()
    {
        SchemaDiffRow[] rows =
        [
            new("Armor.txt", ChangeKind.Removed, 3, "oldcol", ""),
            new("Misc.txt", ChangeKind.Unchanged, null, "", ""),
        ];

        var lines = DiffRowTextFormatter.FormatSchemaRows(rows, "Row", isBatchMode: true);

        Assert.That(
            lines,
            Is.EqualTo(new[] { "Armor.txt", " Removed (Row 3): oldcol", "Misc.txt" })
        );
    }

    [Test]
    public void FormatValueRows_GroupsChangesUnderTheirDataRow()
    {
        ValueDiffRow[] rows =
        [
            new("Armor.txt", "Cap (Row 3)", false, "lvl", "1", "2"),
            new("Armor.txt", "Added (Row 9): Helm", true, "lvl", "", "5"),
        ];

        var lines = DiffRowTextFormatter.FormatValueRows(rows, isBatchMode: false);

        Assert.That(
            lines,
            Is.EqualTo(
                new[]
                {
                    "Cap (Row 3)",
                    " lvl: '1' -> '2'",
                    "",
                    "Added (Row 9): Helm",
                    " lvl: '5'",
                    "",
                }
            )
        );
    }
}
