using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class ConvertServiceTests
{
    private TestFiles _files = null!;
    private static readonly string[] expected = new[] { "knock" };

    [SetUp]
    public void SetUp() => _files = new TestFiles();

    [TearDown]
    public void TearDown() => _files.Dispose();

    private Dictionary<string, List<string>> Convert(
        string[][] source,
        string[][] target,
        RowConversionMode mode,
        string fileName = "Armor.txt"
    )
    {
        _files.Write("src", fileName, source);
        _files.Write("tgt", fileName, target);
        var output = _files.Dir("out");

        ConvertService.ConvertFolder(_files.Dir("src"), _files.Dir("tgt"), output, true, mode);

        return CsvParser.Parse(Path.Combine(output, fileName));
    }

    // Row position is the row ID in D2 files, so no row mode may reorder rows
    [TestCase(RowConversionMode.None)]
    [TestCase(RowConversionMode.AppendOriginalAtEnd)]
    [TestCase(RowConversionMode.AppendTargetAtEnd)]
    public void ConvertFolder_ReproducesFilesExactly_WhenSourceAndTargetAreIdentical(
        RowConversionMode mode
    )
    {
        // Hireling.txt repeats row names heavily (e.g. many "Rogue Scout" rows); the others
        // have "version" 0 rows after 100 rows
        string[] names = ["hireling.txt", "uniqueitems.txt", "magicprefix.txt", "cubemain.txt"];
        foreach (var name in names)
        {
            var bundled = TestFiles.BundledTxt("95041a", name);
            File.Copy(bundled, Path.Combine(_files.Dir("src"), name));
            File.Copy(bundled, Path.Combine(_files.Dir("tgt"), name));
        }
        var output = _files.Dir("out");

        ConvertService.ConvertFolder(_files.Dir("src"), _files.Dir("tgt"), output, true, mode);

        foreach (var name in names)
            Assert.That(
                File.ReadAllBytes(Path.Combine(output, name)),
                Is.EqualTo(File.ReadAllBytes(TestFiles.BundledTxt("95041a", name))),
                name
            );
    }

    [Test]
    public void ConvertFolder_KeepsBlankSourceCells_WhenTargetHasValue()
    {
        // 3.3 -> 3.4 Griswolds Edge: filling only the blank par5 gave prop5=knock with par5=66
        var output = Convert(
            [
                ["name", "prop5", "par5", "min5"],
                ["Griswolds Edge", "knock", "", "1"],
            ],
            [
                ["name", "prop5", "par5", "min5", "newcol"],
                ["Griswolds Edge", "hit-skill", "66", "4", "x"],
            ],
            RowConversionMode.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output["prop5"], Is.EqualTo(expected));
            Assert.That(output["par5"], Is.EqualTo(new[] { "" }));
            Assert.That(output["min5"], Is.EqualTo(new[] { "1" }));
            Assert.That(output["newcol"], Is.EqualTo(new[] { "x" }));
        }
    }

    [Test]
    public void ConvertFolder_KeepsEachDuplicateRowsOwnValues()
    {
        var output = Convert(
            [
                ["name", "lvl"],
                ["Rogue", "1"],
                ["Rogue", "2"],
            ],
            [
                ["name", "lvl"],
                ["Rogue", "1"],
                ["Rogue", "2"],
            ],
            RowConversionMode.None
        );

        Assert.That(output["lvl"], Is.EqualTo(new[] { "1", "2" }));
    }

    [Test]
    public void ConvertFolder_MovesValuesIntoRenamedColumn()
    {
        var output = Convert(
            [
                ["name", "HraltiMin"],
                ["Cap", "7"],
            ],
            [
                ["name", "HratliMin", "newcol"],
                ["Cap", "0", "x"],
            ],
            RowConversionMode.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.Keys, Is.EqualTo(new[] { "name", "HratliMin", "newcol" }));
            Assert.That(output["HratliMin"], Is.EqualTo(new[] { "7" }));
            Assert.That(output["newcol"], Is.EqualTo(new[] { "x" }));
        }
    }

    [Test]
    public void ConvertFolder_AppendsTargetOnlyRows_WhenAppendTargetAtEnd()
    {
        var output = Convert(
            [
                ["name", "v"],
                ["a", "1"],
            ],
            [
                ["name", "v"],
                ["b", "2"],
                ["a", "9"],
            ],
            RowConversionMode.AppendTargetAtEnd
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output["name"], Is.EqualTo(new[] { "a", "b" }));
            Assert.That(output["v"], Is.EqualTo(new[] { "1", "2" }));
        }
    }

    [Test]
    public void ConvertFolder_AppendsSourceOnlyRows_WhenAppendOriginalAtEnd()
    {
        var output = Convert(
            [
                ["name", "v"],
                ["a", "1"],
                ["old", "3"],
            ],
            [
                ["name", "v"],
                ["b", "2"],
                ["a", "9"],
            ],
            RowConversionMode.AppendOriginalAtEnd
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output["name"], Is.EqualTo(new[] { "b", "a", "old" }));
            Assert.That(output["v"], Is.EqualTo(new[] { "2", "1", "3" }));
        }
    }

    [Test]
    public void ConvertFolder_FollowsRowRename_WhenRowNameContainsArrow()
    {
        // CubeMain row names are recipe descriptions like "2 arrows -> bolts"
        var output = Convert(
            [
                ["description", "v"],
                ["2 arrows -> bolts", "1"],
            ],
            [
                ["description", "v"],
                ["2 Arrows -> Bolts", "9"],
            ],
            RowConversionMode.AppendOriginalAtEnd,
            fileName: "CubeMain.txt"
        );

        using (Assert.EnterMultipleScope())
        {
            // One row, not the target row plus the source row appended again
            Assert.That(output["description"], Is.EqualTo(new[] { "2 arrows -> bolts" }));
            Assert.That(output["v"], Is.EqualTo(new[] { "1" }));
        }
    }
}
