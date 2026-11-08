using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class ConvertServiceTests
{
    private TestFiles _files = null!;

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

    [Test]
    public void ConvertFolder_ReproducesFileExactly_WhenSourceAndTargetAreIdentical()
    {
        // Hireling.txt repeats row names heavily (e.g. many "Rogue Scout" rows)
        var hireling = TestFiles.BundledTxt("92777a", "hireling.txt");
        File.Copy(hireling, Path.Combine(_files.Dir("src"), "hireling.txt"));
        File.Copy(hireling, Path.Combine(_files.Dir("tgt"), "hireling.txt"));
        var output = _files.Dir("out");

        ConvertService.ConvertFolder(
            _files.Dir("src"),
            _files.Dir("tgt"),
            output,
            true,
            RowConversionMode.None
        );

        Assert.That(
            File.ReadAllBytes(Path.Combine(output, "hireling.txt")),
            Is.EqualTo(File.ReadAllBytes(hireling))
        );
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

        Assert.That(output.Keys, Is.EqualTo(new[] { "name", "HratliMin", "newcol" }));
        Assert.That(output["HratliMin"], Is.EqualTo(new[] { "7" }));
        Assert.That(output["newcol"], Is.EqualTo(new[] { "x" }));
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

        Assert.That(output["name"], Is.EqualTo(new[] { "a", "b" }));
        Assert.That(output["v"], Is.EqualTo(new[] { "1", "2" }));
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

        Assert.That(output["name"], Is.EqualTo(new[] { "b", "a", "old" }));
        Assert.That(output["v"], Is.EqualTo(new[] { "2", "1", "3" }));
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

        // One row, not the target row plus the source row appended again
        Assert.That(output["description"], Is.EqualTo(new[] { "2 arrows -> bolts" }));
        Assert.That(output["v"], Is.EqualTo(new[] { "1" }));
    }
}
