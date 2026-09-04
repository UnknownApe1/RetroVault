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
    DateTimeOffset Modified,
    bool IsDirectoryGame = false,
    string? IdentityFilePath = null);

public sealed record IdentificationResult(SystemDefinition? System, SystemFormat? Format, double Confidence, string Reason);

public sealed record ScanProgress(long Discovered, long Processed, long Skipped, long Added, long Changed, long Missing, string? CurrentPath);

public sealed record ScanResult(long Discovered, long Processed, long Skipped, long Added, long Changed, long Missing, IReadOnlyList<string> Errors);

public enum LibraryViewFilter { All, Verified, Unverified, ExactDuplicates, MultipleVersions, Missing, NeedsReview, PreferredCopies, Wanted }

public sealed record LibraryFilterOption(LibraryViewFilter Value, string Name);

public sealed record ExistingFileSnapshot(long Id, string FullPath, long Size, DateTimeOffset ModifiedDate, FileStatus Status);

public sealed record GameSummary(long Id, string Title, string System, string SystemKey, int FileCount, int DuplicateCount, int VerifiedCount, FileStatus WorstStatus, string? PreferredCatalogName, string? PreferredRegion, bool IsWanted);

public sealed record LibraryCounts(long Games, long Files, long Duplicates, long Missing, long Verified = 0);

public sealed record LibraryExportRow(
    string System,
    string GameTitle,
    string FileName,
    string FullPath,
    string SourcePath,
    string Format,
    long Size,
    DateTimeOffset ModifiedDate,
    string? Region,
    string? Language,
    string? Revision,
    string? Version,
    FileStatus Status,
    CatalogVerificationStatus CatalogStatus,
    string? CatalogSource,
    string? CatalogName,
    bool IsPreferred,
    bool IsManuallyPreferred,
    bool IsExcluded,
    int PreferenceScore,
    string? QuickHash,
    string? Sha256,
    string? Sha1);

public sealed record CatalogEntry(string Name, string RomName, long Size, string? Crc32, string? Md5, string? Sha1, string? Sha256);

public sealed record VerificationCandidate(long Id, long GameId, string SystemKey, string FullPath, string FileName, string Extension, long Size, FileStatus Status, string? Region, string? Revision, bool IsDirectory = false);

public sealed record CatalogVerificationUpdate(long FileId, CatalogVerificationStatus Status, string Source, string? CatalogName, string? Sha1, string? Crc32, string? Error);

public sealed record CatalogVerificationProgress(int SystemsCompleted, int SystemsTotal, long FilesCompleted, long FilesTotal, string CurrentItem);

public sealed record CatalogVerificationResult(int Systems, long Files, long Verified, long NoMatch, long Unsupported, long Errors);

public sealed record FuzzyMatchCandidate(long GameAId, string GameATitle, long GameBId, string GameBTitle, string SystemName, int FileCountA, int FileCountB, double Similarity);

public sealed record PreferredExportFile(string SystemName, string FullPath, string FileName, bool IsDirectory = false);

public sealed record LibraryExportProgress(long FilesCompleted, long FilesTotal, string CurrentItem);

public sealed record LibraryExportOperationResult(long Copied, long Skipped, long Errors);

public sealed record TitleCleanupCandidate(long GameId, string SystemName, string CurrentTitle, string SuggestedTitle, string Reason);

public sealed record ExactTitleDuplicateGroup(string SystemName, string Title, int TotalFiles, IReadOnlyList<long> GameIds);

public sealed record DuplicateFileRow(string Sha256, string System, string GameTitle, string FileName, string FullPath, long Size);

public sealed class IncompleteFileEnumerationException(string rootPath, int errorCount)
    : IOException($"The scan could not read {errorCount:N0} path(s) under {rootPath}. Indexed changes were saved, but missing-file detection was skipped for safety.")
{
    public string RootPath { get; } = rootPath;
    public int ErrorCount { get; } = errorCount;
}
