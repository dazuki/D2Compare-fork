using System.Text;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class CsvParserTests
{
    private TestFiles _files = null!;

    [SetUp]
    public void SetUp() => _files = new TestFiles();

    [TearDown]
    public void TearDown() => _files.Dispose();

    [Test]
    public void Parse_SuffixesRepeatedHeaders_WhenHeaderNameRepeats()
    {
        var path = _files.Write("src", "a.txt", ["name", "code", "code"], ["x", "1", "2"]);

        var data = CsvParser.Parse(path);

        Assert.That(data.Keys, Is.EqualTo(new[] { "name", "code", "code (2)" }));
        Assert.That(data["code (2)"], Is.EqualTo(new[] { "2" }));
    }

    [Test]
    public void Parse_PadsMissingCells_WhenRowIsShorterThanHeader()
    {
        var path = _files.Write("src", "a.txt", ["name", "a", "b"], ["x"]);

        var data = CsvParser.Parse(path);

        Assert.That(data["b"], Is.EqualTo(new[] { "" }));
    }

    [Test]
    public void Parse_DecodesWindows1252_WhenBytesAreNotValidUtf8()
    {
        // 0x92 is the curly apostrophe in Windows-1252 (as in Legacy UniqueItems.txt)
        var bytes = Encoding
            .ASCII.GetBytes("name\r\nKing")
            .Append((byte)0x92)
            .Append((byte)'s')
            .ToArray();
        var path = _files.WriteRaw("src", "a.txt", bytes);

        var data = CsvParser.Parse(path, out var encoding);

        Assert.That(encoding.CodePage, Is.EqualTo(1252));
        Assert.That(data["name"][0], Is.EqualTo("King’s"));
    }

    [Test]
    public void Parse_StripsUtf8Bom_WhenFileStartsWithBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("name\r\nx\r\n"))
            .ToArray();
        var path = _files.WriteRaw("src", "a.txt", bytes);

        var data = CsvParser.Parse(path, out var encoding);

        Assert.That(data.Keys, Is.EqualTo(new[] { "name" }));
        Assert.That(encoding, Is.InstanceOf<UTF8Encoding>());
    }
}
