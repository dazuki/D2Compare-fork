using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class SchemaFixProviderTests
{
    [TestCase("eol", "*eol")]
    [TestCase("nameable", "Nameable")]
    [TestCase("Immortal King's Soul Cage ", "Immortal King's Soul Cage")]
    public void IsKnownRename_ReturnsTrue_WhenOnlyCaseWhitespaceOrCommentMarkerDiffers(
        string oldName,
        string newName
    ) => Assert.That(SchemaFixProvider.IsKnownRename("any.txt", oldName, newName), Is.True);

    [TestCase("HraltiMagicMin", "HratliMagicMin")]
    [TestCase("HratliMagicMin", "HraltiMagicMin")]
    [TestCase("nehlithakglow2", "nihlathakglow2")]
    public void IsKnownRename_ReturnsTrue_ForTypoFixesInEitherDirection(
        string oldName,
        string newName
    ) => Assert.That(SchemaFixProvider.IsKnownRename("Armor.txt", oldName, newName), Is.True);

    [TestCase("Levels.txt", "MonLvl1", "MonLvl(N)")]
    [TestCase("Armor.txt", "HraltiMin", "HratliMax")]
    [TestCase("ItemTypes.txt", "None", "Company")]
    [TestCase("Shrines.txt", "Shrine Type", "Name")]
    public void IsKnownRename_ReturnsFalse_WhenNamesOnlyShareSubstrings(
        string file,
        string oldName,
        string newName
    ) => Assert.That(SchemaFixProvider.IsKnownRename(file, oldName, newName), Is.False);

    [Test]
    public void IsKnownRename_AppliesFileRuleOnlyToItsFile()
    {
        Assert.That(SchemaFixProvider.IsKnownRename("Weapons.txt", "special", "*comment"), Is.True);
        Assert.That(SchemaFixProvider.IsKnownRename("Armor.txt", "special", "*comment"), Is.False);
    }

    [Test]
    public void IsKnownRename_MatchesFileRuleInReverse_WhenComparingNewerToOlder() =>
        Assert.That(
            SchemaFixProvider.IsKnownRename("MonStats.txt", "TreasureClass", "TreasureClass1"),
            Is.True
        );

    [Test]
    public void IsReusedSlot_RequiresSamePositionAndUnusedName()
    {
        var unused = new IndexedName(204, "unused204");

        Assert.That(
            SchemaFixProvider.IsReusedSlot("ItemStatCost.txt", unused, new(204, "new_stat")),
            Is.True
        );
        Assert.That(
            SchemaFixProvider.IsReusedSlot("ItemStatCost.txt", unused, new(205, "new_stat")),
            Is.False
        );
        Assert.That(
            SchemaFixProvider.IsReusedSlot("Skills.txt", unused, new(204, "new_stat")),
            Is.False
        );
    }
}
