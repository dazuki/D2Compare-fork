using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class DiffRowBuilderTests
{
    [Test]
    public void BuildColumnRows_ListsAddsThenRemovesThenRenames_WithOneBasedPositions()
    {
        var result = CompareResult.Empty("Armor.txt") with
        {
            ChangedColumns = [new Rename(new(1, "HraltiMin"), new(1, "HratliMin"))],
            AddedColumns = [new IndexedName(4, "reqdex")],
            RemovedColumns = [new IndexedName(7, "oldcol")],
        };

        var rows = DiffRowBuilder.BuildColumnRows([result], includeUnchanged: false);

        Assert.That(
            rows,
            Is.EqualTo(
                new[]
                {
                    new SchemaDiffRow("Armor.txt", ChangeKind.Added, 5, "", "reqdex"),
                    new SchemaDiffRow("Armor.txt", ChangeKind.Removed, 8, "oldcol", ""),
                    new SchemaDiffRow("Armor.txt", ChangeKind.Changed, 2, "HraltiMin", "HratliMin"),
                }
            )
        );
    }

    [Test]
    public void BuildRowRows_AddsPlaceholder_OnlyWhenUnchangedFilesAreIncluded()
    {
        var unchanged = CompareResult.Empty("Misc.txt");

        Assert.That(DiffRowBuilder.BuildRowRows([unchanged], includeUnchanged: false), Is.Empty);
        Assert.That(
            DiffRowBuilder.BuildRowRows([unchanged], includeUnchanged: true),
            Is.EqualTo(new[] { new SchemaDiffRow("Misc.txt", ChangeKind.Unchanged, null, "", "") })
        );
    }

    [Test]
    public void BuildValueRows_LabelsRowsAndBlanksMissingSourceValues()
    {
        var result = CompareResult.Empty("Armor.txt") with
        {
            GroupedDifferences =
            [
                new DiffGroup(3, "Cap", [new ValueChange("lvl", "1", "2")]),
                new DiffGroup(9, "Helm", [new ValueChange("lvl", null, "5")], IsNew: true),
            ],
        };

        var rows = DiffRowBuilder.BuildValueRows([result], showOnlyNew: false);

        Assert.That(
            rows,
            Is.EqualTo(
                new[]
                {
                    new ValueDiffRow("Armor.txt", "Cap (Row 3)", false, "lvl", "1", "2"),
                    new ValueDiffRow("Armor.txt", "Added (Row 9): Helm", true, "lvl", "", "5"),
                }
            )
        );
    }

    [Test]
    public void BuildValueRows_KeepsOnlyNewRows_WhenShowOnlyNew()
    {
        var result = CompareResult.Empty("Armor.txt") with
        {
            GroupedDifferences =
            [
                new DiffGroup(3, "Cap", [new ValueChange("lvl", "1", "2")]),
                new DiffGroup(9, "Helm", [new ValueChange("lvl", null, "5")], IsNew: true),
            ],
        };

        var rows = DiffRowBuilder.BuildValueRows([result], showOnlyNew: true);

        Assert.That(rows.Select(r => r.RowLabel), Is.EqualTo(new[] { "Added (Row 9): Helm" }));
    }

    [Test]
    public void BuildValueRows_SkipsFiles_WithoutValueChanges()
    {
        var rows = DiffRowBuilder.BuildValueRows(
            [CompareResult.Empty("Misc.txt")],
            showOnlyNew: false
        );

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void FilterValues_KeepsWholeDataRow_WhenRowLabelMatches()
    {
        ValueDiffRow[] rows =
        [
            new("Armor.txt", "Cap (Row 3)", false, "lvl", "1", "2"),
            new("Armor.txt", "Cap (Row 3)", false, "speed", "0", "5"),
            new("Armor.txt", "Helm (Row 4)", false, "lvl", "3", "4"),
        ];

        Assert.That(DiffRowBuilder.Filter(rows, "cap"), Is.EqualTo(rows[..2]));
        Assert.That(DiffRowBuilder.Filter(rows, "speed"), Is.EqualTo(rows[1..2]));
    }

    [Test]
    public void FilterSchema_DropsUnchangedPlaceholders()
    {
        SchemaDiffRow[] rows =
        [
            new("Misc.txt", ChangeKind.Unchanged, null, "", ""),
            new("Armor.txt", ChangeKind.Changed, 2, "HraltiMin", "HratliMin"),
        ];

        Assert.That(DiffRowBuilder.Filter(rows, "chang"), Is.EqualTo(rows[1..]));
    }

    [Test]
    public void SummarizeValues_CountsDataRows_NotCells()
    {
        ValueDiffRow[] rows =
        [
            new("Armor.txt", "Cap (Row 3)", false, "lvl", "1", "2"),
            new("Armor.txt", "Cap (Row 3)", false, "speed", "0", "5"),
            new("Armor.txt", "Added (Row 9): Helm", true, "lvl", "", "5"),
        ];

        Assert.That(
            DiffRowBuilder.Summarize(rows)["Armor.txt"],
            Is.EqualTo(new ValueFileSummary("Armor.txt", Added: 1, Changed: 1))
        );
    }
}
