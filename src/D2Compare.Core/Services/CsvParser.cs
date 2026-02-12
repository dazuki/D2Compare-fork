namespace D2Compare.Core.Services;

public static class CsvParser
{
    public static Dictionary<string, List<string>> Parse(string filePath)
    {
        var data = new Dictionary<string, List<string>>();

        using var reader = new StreamReader(filePath);
        var headers = reader.ReadLine()!.Split('\t');

        foreach (var header in headers)
            data[header] = new List<string>();

        while (!reader.EndOfStream)
        {
            var values = reader.ReadLine()!.Split('\t');

            for (int i = 0; i < headers.Length; i++)
                data[headers[i]].Add(i < values.Length ? values[i] : "");
        }

        return data;
    }
}