using System.Text;

namespace D2Compare.Core.Services;

public static class CsvParser
{
    static CsvParser()
    {
        // Windows-1252 is not built into .NET on non-Windows platforms
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // Legacy D2 files are Windows-1252; D2R files are UTF-8 (no BOM).
    // Detect per-file so both round-trip correctly.
    public static Encoding DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        try
        {
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            ).GetString(bytes);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252);
        }
    }

    public static Dictionary<string, List<string>> Parse(string filePath)
    {
        var data = new Dictionary<string, List<string>>();

        using var reader = new StreamReader(filePath, DetectEncoding(filePath));
        var headerLine = reader.ReadLine();
        if (headerLine is null)
            return data;
        var rawHeaders = headerLine.Split('\t');

        // Some Legacy files (e.g. 113c/Armor.txt) repeat header names. Without
        // disambiguation duplicates collapse onto one list and misalign every
        // row. Suffix repeats with " (N)"; files with unique headers (all D2R
        // files) are unaffected since no name ever collides.
        var headers = new string[rawHeaders.Length];
        for (int i = 0; i < rawHeaders.Length; i++)
        {
            var resolved = rawHeaders[i];
            int suffix = 1;
            while (data.ContainsKey(resolved))
                resolved = $"{rawHeaders[i]} ({++suffix})";

            headers[i] = resolved;
            data[resolved] = new List<string>();
        }

        while (!reader.EndOfStream)
        {
            var values = reader.ReadLine()!.Split('\t');

            for (int i = 0; i < headers.Length; i++)
                data[headers[i]].Add(i < values.Length ? values[i] : "");
        }

        return data;
    }
}
