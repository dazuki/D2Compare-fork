using System.Text;

namespace D2Compare.Core.Services;

public static class CsvParser
{
    static CsvParser()
    {
        // Windows-1252 is not built into .NET on non-Windows platforms
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    // Legacy D2 files are Windows-1252; D2R files are UTF-8 (no BOM).
    // Detect per-file so both round-trip correctly.
    public static Encoding DetectEncoding(string filePath) =>
        Decode(File.ReadAllBytes(filePath)).Encoding;

    public static Dictionary<string, List<string>> Parse(string filePath) => Parse(filePath, out _);

    public static Dictionary<string, List<string>> Parse(string filePath, out Encoding encoding)
    {
        var (text, detected) = Decode(File.ReadAllBytes(filePath));
        encoding = detected;
        return ParseText(text);
    }

    private static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        try
        {
            // Mod files saved by some editors carry a BOM; strip it like StreamReader would
            var text = s_strictUtf8.GetString(bytes).TrimStart('﻿');
            return (text, new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            var cp1252 = Encoding.GetEncoding(1252);
            return (cp1252.GetString(bytes), cp1252);
        }
    }

    private static Dictionary<string, List<string>> ParseText(string text)
    {
        var data = new Dictionary<string, List<string>>();

        using var reader = new StringReader(text);
        var headerLine = reader.ReadLine();
        if (headerLine is null)
            return data;
        var rawHeaders = headerLine.Split('\t');

        // Some Legacy files (e.g. 113c/Armor.txt) repeat header names. Without
        // disambiguation duplicates collapse onto one list and misalign every
        // row. Suffix repeats with " (N)"; files with unique headers (all D2R
        // files) are unaffected since no name ever collides.
        var columns = new List<string>[rawHeaders.Length];
        for (int i = 0; i < rawHeaders.Length; i++)
        {
            var resolved = rawHeaders[i];
            int suffix = 1;
            while (data.ContainsKey(resolved))
                resolved = $"{rawHeaders[i]} ({++suffix})";

            columns[i] = new List<string>();
            data[resolved] = columns[i];
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var values = line.Split('\t');
            for (int i = 0; i < columns.Length; i++)
                columns[i].Add(i < values.Length ? values[i] : "");
        }

        return data;
    }
}
