namespace D2Compare.Core.Models;

public record VersionInfo(string DisplayName, string FolderName)
{
    public static readonly VersionInfo[] BuiltInVersions =
    [
        new("Legacy (1.13c+)", "113c"),
        new("1.0.0.0 (62115)", "62115"),
        new("1.4.0.0 (64954)", "64954"),
        new("2.2.0.0 (65890)", "65890"),
        new("2.3.0.0 (67314)", "67314"),
        new("2.3.0.1 (67358)", "67358"),
        new("2.3.1.0 (67554)", "67554"),
        new("2.4.1.1 (68992)", "68992"),
        new("2.4.1.2 (69270)", "69270"),
        new("2.4.3.0 (70161)", "70161"),
        new("2.5.0.0 (71336)", "71336"),
        new("2.5.1.0 (71510)", "71510"),
        new("2.5.2.0 (71776)", "71776"),
        new("2.6.0.0 (73090)", "73090"),
        new("2.7.2.0 (77312)", "77312"),
        new("2.7.3.0 (80273)", "80273"),
        new("2.7.4.0 (81914)", "81914"),
        new("2.8.0.0 (83721)", "83721"),
        new("2.9.0.0 (90471)", "90471"),
        new("3.0.0.0 (91636)", "91636"),
    ];

    public string GetPath() => Path.Combine("TXT", FolderName);
}