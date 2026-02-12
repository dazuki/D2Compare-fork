namespace D2Compare.Core.Models;

public record FileListResult(
    IReadOnlyList<string> CommonFiles,
    IReadOnlyList<string> SourceOnly,
    IReadOnlyList<string> TargetOnly
);