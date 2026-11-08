namespace D2Compare.Core.Services;

public readonly record struct RowInstanceKey(string Name, int Occurrence);

public static class RowInstanceKeyHelper
{
    public static List<RowInstanceKey> BuildKeys(IReadOnlyList<string> rowNames)
    {
        var occurrenceByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new List<RowInstanceKey>(rowNames.Count);

        foreach (var rowName in rowNames)
        {
            occurrenceByName.TryGetValue(rowName, out var occurrence);
            occurrence++;
            occurrenceByName[rowName] = occurrence;

            keys.Add(new RowInstanceKey(rowName, occurrence));
        }

        return keys;
    }

    public static Dictionary<RowInstanceKey, int> IndexByKey(IReadOnlyList<RowInstanceKey> keys)
    {
        var indices = new Dictionary<RowInstanceKey, int>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
            indices[keys[i]] = i;
        return indices;
    }
}
