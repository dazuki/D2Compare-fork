namespace D2Compare.Core.Services;

// Recognizes known schema transformations between D2 versions that look like
// adds/removes but are actually renames. Ported from Form1.ApplyManualFixes().
public static class SchemaFixProvider
{
    private static readonly Dictionary<string, string> MonstatsMappings = new()
    {
        { "ShieldBlockOverride", "NoShldBlock" },
        { "TreasureClass", "TreasureClass1" },
        { "TreasureClassChamp", "TreasureClass2" },
        { "TreasureClassUnique", "TreasureClass3" },
        { "TreasureClassQuest", "TreasureClass4" },
        { "TreasureClass(N)", "TreasureClass1(N)" },
        { "TreasureClassChamp(N)", "TreasureClass2(N)" },
        { "TreasureClassUnique(N)", "TreasureClass3(N)" },
        { "TreasureClassQuest(N)", "TreasureClass4(N)" },
        { "TreasureClass(H)", "TreasureClass1(H)" },
        { "TreasureClassChamp(H)", "TreasureClass2(H)" },
        { "TreasureClassUnique(H)", "TreasureClass3(H)" },
        { "TreasureClassQuest(H)", "TreasureClass4(H)" },
    };

    private static readonly List<string> ItemStatCostAdded =
    [
        "lasthitreactframe", "create_season", "bonus_mindamage", "bonus_maxdamage",
        "item_pierce_cold_immunity", "item_pierce_fire_immunity", "item_pierce_light_immunity",
        "item_pierce_poison_immunity", "item_pierce_damage_immunity", "item_pierce_magic_immunity",
        "item_charge_noconsume", "modifierlist_castid", "item_noconsume",
        "passive_mastery_noconsume", "passive_mastery_replenish_oncrit",
        "passive_mastery_gethit_rate", "passive_mastery_attack_speed",
    ];

    private static readonly List<string> ItemStatCostRemoved =
    [
        "unused183", "unused184", "unused185", "unused186", "unused187", "unused189",
        "unused190", "unused191", "unused192", "unused193", "unused200", "unused202",
        "unused204", "unused205", "unused206", "unused207", "unused212",
    ];

    public static bool IsKnownRename(string addedHeader, string removedHeader, string fileType)
    {
        // Comment column markers (* prefix)
        if (addedHeader.ToLower().Replace("*", "") == removedHeader.ToLower().Replace("*", ""))
            return true;

        // NPC typo/name changes
        if ((Contains(addedHeader, "hratli") && Contains(removedHeader, "hralti")) ||
            (Contains(addedHeader, "anya") && Contains(removedHeader, "drehya")))
            return true;

        // CubeMain hotfix
        if (Contains(addedHeader, "firstLadderSeason") && Contains(removedHeader, "ladder"))
            return true;

        // DifficultyLevels hotfix
        if (Contains(addedHeader, "MercenaryDamagePercentVSBoss") && Contains(removedHeader, "HireableBossDamagePercent"))
            return true;

        // ItemStatCost
        if ((ItemStatCostAdded.Contains(addedHeader, StringComparer.OrdinalIgnoreCase) &&
             ItemStatCostRemoved.Contains(removedHeader, StringComparer.OrdinalIgnoreCase)) ||
            (ItemStatCostAdded.Contains(removedHeader, StringComparer.OrdinalIgnoreCase) &&
             ItemStatCostRemoved.Contains(addedHeader, StringComparer.OrdinalIgnoreCase)))
            return true;

        // Itemtypes hotfix
        if ((Contains(addedHeader, "MaxSockets1") && Contains(removedHeader, "MaxSock1")) ||
            (Contains(addedHeader, "MaxSockets2") && Contains(removedHeader, "MaxSock25")) ||
            (Contains(addedHeader, "MaxSockets3") && Contains(removedHeader, "MaxSock40")) ||
            (Contains(addedHeader, "Any") && Contains(removedHeader, "None")))
            return true;

        // Levels hotfix
        if ((Contains(addedHeader, "MonLvl") && Contains(removedHeader, "MonLvl1")) ||
            (Contains(addedHeader, "MonLvl(N)") && Contains(removedHeader, "MonLvl2")) ||
            (Contains(addedHeader, "MonLvl(H)") && Contains(removedHeader, "MonLvl3")) ||
            (Contains(addedHeader, "MonLvlEx") && Contains(removedHeader, "MonLvl1Ex")) ||
            (Contains(addedHeader, "MonLvlEx(N)") && Contains(removedHeader, "MonLvl2Ex")) ||
            (Contains(addedHeader, "MonLvlEx(H)") && Contains(removedHeader, "MonLvl3Ex")))
            return true;

        // Missiles hotfix (nihlathak/nehlithak typo)
        string[] nihlathakSuffixes = ["control", "swoosh", "debris1", "debris2", "debris3", "debris4", "glow", "hole", "holelight", "glow2", "bonechips"];
        foreach (var suffix in nihlathakSuffixes)
        {
            if (Contains(addedHeader, "nihlathak" + suffix) && Contains(removedHeader, "nehlithak" + suffix))
                return true;
        }

        // Monstats hotfix
        foreach (var mapping in MonstatsMappings)
        {
            if ((addedHeader.Equals(mapping.Key, StringComparison.OrdinalIgnoreCase) &&
                 removedHeader.Equals(mapping.Value, StringComparison.OrdinalIgnoreCase)) ||
                (addedHeader.Equals(mapping.Value, StringComparison.OrdinalIgnoreCase) &&
                 removedHeader.Equals(mapping.Key, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Objects hotfix
        if (Contains(addedHeader, "*Description") && Contains(removedHeader, "description - not loaded"))
            return true;

        // Runes hotfix
        if ((Contains(addedHeader, "*RunesUsed") && Contains(removedHeader, "*runes")) ||
            (Contains(addedHeader, "firstLadderSeason") && Contains(removedHeader, "server")))
            return true;

        // SetItems hotfix
        if (Contains(addedHeader, "*ItemName") && Contains(removedHeader, "*item"))
            return true;

        // Shrines hotfix
        if ((Contains(addedHeader, "Name") && Contains(removedHeader, "Shrine Type")) ||
            (Contains(addedHeader, "*Shrine Type") && Contains(removedHeader, "Shrine name")) ||
            (Contains(addedHeader, "*Effect") && Contains(removedHeader, "Effect")))
            return true;

        // TreasureClassEx hotfix
        if ((Contains(addedHeader, "*ItemProbSum") && Contains(removedHeader, "SumItems")) ||
            (Contains(addedHeader, "*ItemProbTotal") && Contains(removedHeader, "TotalProb")) ||
            (Contains(addedHeader, "*TreasureClassDropChance") && Contains(removedHeader, "DropChance")) ||
            (Contains(addedHeader, "*eol") && Contains(removedHeader, "Term")))
            return true;

        // UniqueItems hotfix
        if ((Contains(addedHeader, "*ItemName") && Contains(removedHeader, "*type")) ||
            (Contains(addedHeader, "*Shrine Type") && Contains(removedHeader, "Shrine name")) ||
            (Contains(addedHeader, "*Effect") && Contains(removedHeader, "Effect")))
            return true;

        // Weapons hotfix
        if (Contains(addedHeader, "*comment") && Contains(removedHeader, "special"))
            return true;

        return false;
    }

    private static bool Contains(string source, string value) =>
        source.IndexOf(value, StringComparison.OrdinalIgnoreCase) != -1;
}