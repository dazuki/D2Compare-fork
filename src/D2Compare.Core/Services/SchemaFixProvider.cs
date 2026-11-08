using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

// Recognizes known schema transformations between D2 versions that look like
// adds/removes but are actually renames. Every rule is exact and works in both
// directions, so comparing a newer version against an older one also matches.
public static class SchemaFixProvider
{
    // Per-file renames, keyed by file name without extension
    private static readonly Dictionary<string, (string Old, string New)[]> FileRenames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["cubemain"] = [("ladder", "firstLadderSeason")],
        ["difficultylevels"] = [("HireableBossDamagePercent", "MercenaryDamagePercentVSBoss")],
        ["events"] = [("*desc", "*description")],
        ["itemtypes"] =
        [
            ("MaxSock1", "MaxSockets1"),
            ("MaxSock25", "MaxSockets2"),
            ("MaxSock40", "MaxSockets3"),
        ],
        ["levels"] =
        [
            ("MonLvl1", "MonLvl"),
            ("MonLvl2", "MonLvl(N)"),
            ("MonLvl3", "MonLvl(H)"),
            ("MonLvl1Ex", "MonLvlEx"),
            ("MonLvl2Ex", "MonLvlEx(N)"),
            ("MonLvl3Ex", "MonLvlEx(H)"),
        ],
        ["misscalc"] = [("*desc", "*description")],
        ["monstats"] =
        [
            ("NoShldBlock", "ShieldBlockOverride"),
            ("TreasureClass1", "TreasureClass"),
            ("TreasureClass2", "TreasureClassChamp"),
            ("TreasureClass3", "TreasureClassUnique"),
            ("TreasureClass4", "TreasureClassQuest"),
            ("TreasureClass1(N)", "TreasureClass(N)"),
            ("TreasureClass2(N)", "TreasureClassChamp(N)"),
            ("TreasureClass3(N)", "TreasureClassUnique(N)"),
            ("TreasureClass4(N)", "TreasureClassQuest(N)"),
            ("TreasureClass1(H)", "TreasureClass(H)"),
            ("TreasureClass2(H)", "TreasureClassChamp(H)"),
            ("TreasureClass3(H)", "TreasureClassUnique(H)"),
            ("TreasureClass4(H)", "TreasureClassQuest(H)"),
        ],
        ["objects"] = [("description - not loaded", "*Description")],
        ["properties"] = [("*done", "*Enabled"), ("*desc", "*Tooltip"), ("*param", "*Parameter")],
        ["runes"] = [("*runes", "*RunesUsed"), ("server", "firstLadderSeason")],
        ["setitems"] = [("*item", "*ItemName")],
        ["shrines"] = [("Shrine name", "Name")],
        ["skillcalc"] = [("*desc", "*Description")],
        ["treasureclassex"] =
        [
            ("SumItems", "*ItemProbSum"),
            ("TotalProb", "*ItemProbTotal"),
            ("DropChance", "*TreasureClassDropChance"),
            ("Term", "*eol"),
            ("ladder", "firstLadderSeason"),
        ],
        ["uniqueitems"] = [("*type", "*ItemName"), ("ladder", "firstLadderSeason")],
        ["weapons"] = [("special", "*comment")],
    };

    // Typos fixed in D2R (NPC names in Armor/Misc/Weapons, Nihlathak missiles)
    private static readonly (string Old, string New)[] Substitutions =
    [
        ("Hralti", "Hratli"),
        ("Drehya", "Anya"),
        ("nehlithak", "nihlathak"),
    ];

    // Files where retired "unusedNNN" row slots get reused for new entries at the same position
    private static readonly HashSet<string> SlotReuseFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "itemstatcost",
    };

    // Names that only differ by case, whitespace or the "*" comment marker are the same entry
    public static string Canonical(string name) => name.Replace("*", "").Trim().ToLowerInvariant();

    // Canonical names a renamed counterpart of `name` may have in the other version
    public static IEnumerable<string> RenameKeys(string fileName, string name)
    {
        var canonical = Canonical(name);
        yield return canonical;

        foreach (var (oldPart, newPart) in Substitutions)
        {
            if (name.Contains(oldPart, StringComparison.OrdinalIgnoreCase))
                yield return Canonical(
                    name.Replace(oldPart, newPart, StringComparison.OrdinalIgnoreCase)
                );
            if (name.Contains(newPart, StringComparison.OrdinalIgnoreCase))
                yield return Canonical(
                    name.Replace(newPart, oldPart, StringComparison.OrdinalIgnoreCase)
                );
        }

        if (!FileRenames.TryGetValue(FileKey(fileName), out var renames))
            yield break;

        foreach (var (oldName, newName) in renames)
        {
            if (Canonical(oldName) == canonical)
                yield return Canonical(newName);
            if (Canonical(newName) == canonical)
                yield return Canonical(oldName);
        }
    }

    public static bool IsKnownRename(string fileName, string oldName, string newName) =>
        RenameKeys(fileName, oldName).Contains(Canonical(newName));

    // A retired slot ("unusedNNN") replaced by a new entry at the same position
    public static bool IsReusedSlot(string fileName, IndexedName removed, IndexedName added) =>
        SlotReuseFiles.Contains(FileKey(fileName))
        && removed.Index == added.Index
        && (IsUnused(removed.Name) || IsUnused(added.Name));

    private static bool IsUnused(string name) =>
        name.StartsWith("unused", StringComparison.OrdinalIgnoreCase);

    private static string FileKey(string fileName) => Path.GetFileNameWithoutExtension(fileName);
}
