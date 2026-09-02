namespace RomManager.Core.Models;

public sealed record ParsedFileName(
    string Title,
    string NormalizedTitle,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Languages,
    string? Revision,
    string? Version,
    int? DiscNumber,
    int? TrackNumber,
    IReadOnlyList<string> Tags);

public sealed record FileCandidate(
    string FullPath,
    string FileName,
    string Extension,
    long Size,
    DateTimeOffset Created,
    DateTimeOffset Modified);

public sealed record IdentificationResult(SystemDefinition? System, SystemFormat? Format, double Confidence, string Reason);

public sealed record ScanProgress(long Discovered, long Processed, long Skipped, long Added, long Changed, long Missing, string? CurrentPath);

public sealed record ScanResult(long Discovered, long Processed, long Skipped, long Added, long Changed, long Missing, IReadOnlyList<string> Errors);

public sealed record GameSummary(long Id, string Title, string System, int FileCount, int DuplicateCount, FileStatus WorstStatus);

public sealed record LibraryCounts(long Games, long Files, long Duplicates, long Missing);
