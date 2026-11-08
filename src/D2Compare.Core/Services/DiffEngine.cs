using System.Collections.Concurrent;
using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

public static class DiffEngine
{
    public static List<DiffGroup> GetGroupedDifferences(
        Dictionary<string, List<string>> sourceData,
        Dictionary<string, List<string>> targetData,
        string rowHeaderColumn,
        bool includeNewRows
    )
    {
        // Source column order first, then target-only columns; resolved once instead of per cell
        var columns = sourceData
            .Keys.Concat(targetData.Keys.Where(k => !sourceData.ContainsKey(k)))
            .Select(name =>
                (
                    Name: name,
                    Source: sourceData.GetValueOrDefault(name),
                    Target: targetData.GetValueOrDefault(name)
                )
            )
            .ToList();

        var sourceRowIndices = RowInstanceKeyHelper.IndexByKey(
            RowInstanceKeyHelper.BuildKeys(sourceData[rowHeaderColumn])
        );
        var targetRowKeys = RowInstanceKeyHelper.BuildKeys(targetData[rowHeaderColumn]);

        var groups = new ConcurrentBag<(DiffGroup Group, int SourceOrder)>();

        // Each row instance yields at most one group, so rows are processed independently
        Parallel.For(
            0,
            targetRowKeys.Count,
            targetIndex =>
            {
                var rowKey = targetRowKeys[targetIndex];
                if (sourceRowIndices.TryGetValue(rowKey, out var sourceIndex))
                {
                    var changes = ChangedValues(columns, sourceIndex, targetIndex);
                    if (changes.Count > 0)
                    {
                        var row = Math.Min(sourceIndex, targetIndex) + 1;
                        groups.Add((new DiffGroup(row, rowKey.Name, changes), sourceIndex));
                    }
                }
                else if (includeNewRows)
                {
                    var changes = NewRowValues(columns, targetIndex);
                    groups.Add(
                        (
                            new DiffGroup(targetIndex + 1, rowKey.Name, changes, IsNew: true),
                            int.MaxValue
                        )
                    );
                }
            }
        );

        return groups
            .OrderBy(g => g.Group.Row)
            .ThenBy(g => g.SourceOrder)
            .ThenBy(g => g.Group.RowName, StringComparer.Ordinal)
            .Select(g => g.Group)
            .ToList();
    }

    private static List<ValueChange> ChangedValues(
        List<(string Name, List<string>? Source, List<string>? Target)> columns,
        int sourceIndex,
        int targetIndex
    )
    {
        var changes = new List<ValueChange>();
        foreach (var (name, source, target) in columns)
        {
            if (source is null || target is null)
                continue;

            var oldValue = source[sourceIndex];
            var newValue = target[targetIndex];
            if (oldValue != newValue)
                changes.Add(new ValueChange(name, oldValue, newValue));
        }
        return changes;
    }

    private static List<ValueChange> NewRowValues(
        List<(string Name, List<string>? Source, List<string>? Target)> columns,
        int targetIndex
    )
    {
        var changes = new List<ValueChange>();
        foreach (var (name, _, target) in columns)
        {
            if (target is not null)
                changes.Add(new ValueChange(name, null, target[targetIndex]));
        }
        return changes;
    }
}
