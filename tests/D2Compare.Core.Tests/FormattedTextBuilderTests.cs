using D2Compare.Core.Models;
using D2Compare.Core.Services;

namespace D2Compare.Core.Tests;

public class FormattedTextBuilderTests
{
    private static string Text(FormattedLine line) => string.Concat(line.Spans.Select(s => s.Text));

    [Test]
    public void BuildColumnDiffs_ShowsOneBasedColumnNumbers()
    {
        var result = CompareResult.Empty("Armor.txt") with
        {
            ChangedColumns = [new Rename(new(1, "HraltiMin"), new(1, "HratliMin"))],
            AddedColumns = [new IndexedName(4, "reqdex")],
        };

        var lines = FormattedTextBuilder
            .BuildColumnDiffs(result, isBatchMode: false)
            .Lines.Select(Text);

        Assert.That(
            lines,
            Is.EqualTo(new[] { "Changed (Col 2): HraltiMin -> HratliMin", "Added (Col 5): reqdex" })
        );
    }

    [Test]
    public void BuildValueDiffs_ShowsOldAndNewValues_AndNewRowsSeparately()
    {
        var result = CompareResult.Empty("Armor.txt") with
        {
            GroupedDifferences =
            [
                new DiffGroup(3, "Cap", [new ValueChange("lvl", "1", "2")]),
                new DiffGroup(9, "Helm", [new ValueChange("lvl", null, "5")], IsNew: true),
            ],
        };

        var lines = FormattedTextBuilder
            .BuildValueDiffs(result, isBatchMode: false)
            .Lines.Select(Text);

        Assert.That(
            lines,
            Is.EqualTo(
                new[]
                {
                    "Cap (Row 3)",
                    " lvl: '1' -> '2'",
                    "",
                    "Added (Row 9): Helm",
                    " lvl: '5'",
                    "",
                }
            )
        );
    }

    [Test]
    public void BuildValueDiffs_MutesFileName_WhenBatchFileHasNoChanges()
    {
        var doc = FormattedTextBuilder.BuildValueDiffs(
            CompareResult.Empty("Armor.txt"),
            isBatchMode: true
        );

        Assert.That(doc.Lines.Single().Spans.Single().Color, Is.EqualTo(SpanColor.FileNameMuted));
    }
}
