using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class CompareServiceTests
{
    private TestFiles _files = null!;

    [SetUp]
    public void SetUp() => _files = new TestFiles();

    [TearDown]
    public void TearDown() => _files.Dispose();

    private CompareResult Compare(
        string[][] source,
        string[][] target,
        bool includeNewRows = false
    ) =>
        CompareService.CompareFile(
            _files.Write("src", "Armor.txt", source),
            _files.Write("tgt", "Armor.txt", target),
            includeNewRows
        );

    [Test]
    public void CompareFile_ReportsNothing_WhenFilesAreIdentical()
    {
        string[][] rows =
        [
            ["name", "code"],
            ["Cap", "cap"],
            ["Cap", "cap"],
        ];

        var result = Compare(rows, rows, includeNewRows: true);

        Assert.That(result.HasColumnChanges, Is.False);
        Assert.That(result.HasRowChanges, Is.False);
        Assert.That(result.GroupedDifferences, Is.Empty);
    }

    [Test]
    public void CompareFile_PairsEachRemovedColumnWithExactlyOneAddedColumn()
    {
        var result = Compare(
            [
                ["name", "HraltiMin", "HraltiMax", "HraltiMagicMin"],
                ["Cap", "1", "2", "3"],
            ],
            [
                ["name", "HratliMin", "HratliMax", "HratliMagicMin"],
                ["Cap", "1", "2", "3"],
            ]
        );

        var pairs = result.ChangedColumns.Select(r => (r.Old.Name, r.New.Name));
        Assert.That(
            pairs,
            Is.EqualTo(
                new[]
                {
                    ("HraltiMin", "HratliMin"),
                    ("HraltiMax", "HratliMax"),
                    ("HraltiMagicMin", "HratliMagicMin"),
                }
            )
        );
        Assert.That(result.AddedColumns, Is.Empty);
        Assert.That(result.RemovedColumns, Is.Empty);
    }

    [Test]
    public void CompareFile_ReportsAddAndRemove_WhenColumnsAreUnrelatedEvenIfCountsMatch()
    {
        var result = Compare(
            [
                ["name", "strsing"],
                ["a", "1"],
            ],
            [
                ["name", "element"],
                ["a", "1"],
            ]
        );

        Assert.That(result.ChangedColumns, Is.Empty);
        Assert.That(result.RemovedColumns.Single(), Is.EqualTo(new IndexedName(1, "strsing")));
        Assert.That(result.AddedColumns.Single(), Is.EqualTo(new IndexedName(1, "element")));
    }

    [Test]
    public void CompareFile_ComparesDuplicateRowNamesByOccurrence()
    {
        var result = Compare(
            [
                ["name", "lvl"],
                ["Rogue", "1"],
                ["Rogue", "2"],
            ],
            [
                ["name", "lvl"],
                ["Rogue", "1"],
                ["Rogue", "5"],
                ["Rogue", "9"],
            ],
            includeNewRows: true
        );

        Assert.That(result.AddedRows.Single(), Is.EqualTo(new IndexedName(2, "Rogue")));
        var changed = result.GroupedDifferences.Single(g => !g.IsNew);
        Assert.That(changed.Row, Is.EqualTo(2));
        Assert.That(changed.Changes.Single(), Is.EqualTo(new ValueChange("lvl", "2", "5")));
        var added = result.GroupedDifferences.Single(g => g.IsNew);
        Assert.That(added.Changes, Does.Contain(new ValueChange("lvl", null, "9")));
    }

    [Test]
    public void CompareFile_OmitsNewRowValues_WhenIncludeNewRowsIsOff()
    {
        var result = Compare(
            [
                ["name", "lvl"],
                ["a", "1"],
            ],
            [
                ["name", "lvl"],
                ["a", "1"],
                ["b", "2"],
            ]
        );

        Assert.That(result.AddedRows, Has.Count.EqualTo(1));
        Assert.That(result.GroupedDifferences, Is.Empty);
    }

    [Test]
    public void CompareFile_OrdersValueGroupsByRow()
    {
        var result = Compare(
            [
                ["name", "v"],
                ["a", "1"],
                ["b", "1"],
                ["c", "1"],
            ],
            [
                ["name", "v"],
                ["a", "2"],
                ["b", "2"],
                ["c", "2"],
            ]
        );

        Assert.That(result.GroupedDifferences.Select(g => g.Row), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void CompareFile_BundledLegacyArmor_MatchesNpcRenamesOneToOne()
    {
        var result = CompareService.CompareFile(
            TestFiles.BundledTxt("113c", "Armor.txt"),
            TestFiles.BundledTxt("62115", "armor.txt"),
            includeNewRows: false
        );

        var oldNames = result.ChangedColumns.Select(r => r.Old.Name).ToList();
        var newNames = result.ChangedColumns.Select(r => r.New.Name).ToList();
        Assert.That(oldNames, Is.Unique);
        Assert.That(newNames, Is.Unique);
        Assert.That(
            result.ChangedColumns.Select(r => (r.Old.Name, r.New.Name)),
            Does.Contain(("HraltiMagicMax", "HratliMagicMax"))
                .And.Contain(("DrehyaMagicLvl", "AnyaMagicLvl"))
        );
        Assert.That(result.RemovedColumns.Select(c => c.Name), Has.None.StartsWith("Hralti"));
    }

    [Test]
    public void CompareFolder_MatchesFilesCaseInsensitively_AndSkipsUnmatched()
    {
        _files.Write("src", "Armor.txt", ["name"], ["a"]);
        _files.Write("src", "OnlySource.txt", ["name"], ["a"]);
        _files.Write("tgt", "armor.TXT", ["name"], ["a"]);

        var results = CompareService.CompareFolder(_files.Dir("src"), _files.Dir("tgt"), false);

        Assert.That(results.Select(r => r.FileName), Is.EqualTo(new[] { "Armor.txt" }));
    }
}
