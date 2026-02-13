using System.Text.RegularExpressions;

using D2Compare.Core.Models;

namespace D2Compare.Core.Services;

public static class DiffEngine
{
    public static Dictionary<string, int> GetRemovedRows(
        Dictionary<string, List<string>> file1Data,
        Dictionary<string, List<string>> file2Data,
        string rowHeaderColumn)
    {
        var removedRows = new Dictionary<string, int>();

        foreach (var row in file1Data[rowHeaderColumn])
        {
            if (removedRows.ContainsKey(row))
                removedRows[row]++;
            else
                removedRows[row] = 1;
        }

        foreach (var row in file2Data[rowHeaderColumn])
        {
            if (removedRows.ContainsKey(row))
            {
                removedRows[row]--;
                if (removedRows[row] == 0)
                    removedRows.Remove(row);
            }
        }

        return removedRows;
    }

    public static List<string> ExpandCounts(Dictionary<string, int> dictionary)
    {
        var list = new List<string>();
        foreach (var kvp in dictionary)
        {
            for (int i = 0; i < kvp.Value; i++)
                list.Add(kvp.Key);
        }
        return list;
    }

    public static List<DiffGroup> GetGroupedDifferences(
        Dictionary<string, List<string>> file1Data,
        Dictionary<string, List<string>> file2Data,
        HashSet<string> allHeaders,
        string rowHeaderColumn,
        bool includeNewRows)
    {
        var groupedDifferences = new Dictionary<string, List<(string Diff, string ColIndex)>>();
        var newRowKeys = new HashSet<string>();

        var allRowHeaders = new HashSet<string>(file1Data[rowHeaderColumn]);
        allRowHeaders.UnionWith(file2Data[rowHeaderColumn]);

        Parallel.ForEach(allRowHeaders, rowHeader =>
        {
            bool inFile1 = file1Data[rowHeaderColumn].Contains(rowHeader);
            bool inFile2 = file2Data[rowHeaderColumn].Contains(rowHeader);

            if (inFile1 && inFile2)
            {
                foreach (var header in allHeaders)
                {
                    if (!file1Data.ContainsKey(header) || !file2Data.ContainsKey(header))
                        continue;

                    int index1 = file1Data[rowHeaderColumn].IndexOf(rowHeader);
                    int index2 = file2Data[rowHeaderColumn].IndexOf(rowHeader);

                    if (index1 == -1 || index2 == -1)
                        continue;

                    var value1 = file1Data[header][index1];
                    var value2 = file2Data[header][index2];

                    if (value1 != value2)
                    {
                        string valueDifference = $"{header}: '{value1}' -> '{value2}'";
                        string column0Value = $"(Row {Math.Min(index1, index2) + 1}) {rowHeader}";
                        int columnIndex = allHeaders.ToList().IndexOf(header);

                        lock (groupedDifferences)
                        {
                            if (!groupedDifferences.ContainsKey(column0Value))
                                groupedDifferences[column0Value] = new List<(string, string)>();

                            groupedDifferences[column0Value].Add((valueDifference, columnIndex.ToString()));
                        }
                    }
                }
            }
            else if (includeNewRows && !inFile1)
            {
                foreach (var header in allHeaders)
                {
                    if (!file2Data.ContainsKey(header))
                        continue;

                    int index2 = file2Data[rowHeaderColumn].IndexOf(rowHeader);
                    if (index2 == -1)
                        continue;

                    var value2 = file2Data[header][index2];

                    string valueDifference = $"{header}: '{value2}'";
                    string column0Value = $"(Row {index2 + 1}) {rowHeader}";
                    int columnIndex = allHeaders.ToList().IndexOf(header);

                    lock (groupedDifferences)
                    {
                        if (!groupedDifferences.ContainsKey(column0Value))
                            groupedDifferences[column0Value] = new List<(string, string)>();

                        groupedDifferences[column0Value].Add((valueDifference, columnIndex.ToString()));
                        newRowKeys.Add(column0Value);
                    }
                }
            }
        });

        return groupedDifferences
            .OrderBy(pair => int.Parse(Regex.Match(pair.Key, @"\(Row (\d+)\)").Groups[1].Value))
            .Select(pair =>
            {
                var sorted = pair.Value.OrderBy(t => int.Parse(t.ColIndex)).ToList();
                return new DiffGroup(pair.Key, sorted.Select(t => t.Diff).ToList(), newRowKeys.Contains(pair.Key));
            })
            .ToList();
    }
}