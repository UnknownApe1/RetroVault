using RomManager.Core.Models;

namespace RomManager.Core.Services;

public interface IFileSystem
{
    IAsyncEnumerable<FileCandidate> EnumerateFilesAsync(string root, bool recursive, CancellationToken cancellationToken);
    ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
    bool DirectoryExists(string path);
}

public interface IFileNameParser { ParsedFileName Parse(string fileName); }
public interface IHashService
{
    Task<string> ComputeQuickHashAsync(string path, long size, CancellationToken cancellationToken);
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
}
public interface ICatalogVerificationService
{
    Task<CatalogVerificationResult> VerifyAsync(string? systemKey, bool includeAlreadyChecked, IProgress<CatalogVerificationProgress>? progress, CancellationToken cancellationToken);
}
public interface IThumbnailService
{
    Task<string?> GetThumbnailPathAsync(string systemKey, string gameTitle, CancellationToken cancellationToken);
}
public interface ILibraryExportService
{
    Task<LibraryExportOperationResult> ExportPreferredCopiesAsync(string destinationRoot, bool onlyWanted, IProgress<LibraryExportProgress>? progress, CancellationToken cancellationToken);
}
public interface IFormatIdentifier { IdentificationResult Identify(FileCandidate file); bool IsCandidate(string extension); }
public interface IArchiveInspector { Task<IdentificationResult?> IdentifyContentsAsync(FileCandidate archive, CancellationToken cancellationToken); }

public interface ILibraryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanLocation>> GetScanLocationsAsync(bool enabledOnly, CancellationToken cancellationToken);
    Task<ScanLocation> AddScanLocationAsync(string path, bool recursive, CancellationToken cancellationToken);
    Task UpdateScanLocationAsync(int id, bool enabled, bool recursive, CancellationToken cancellationToken);
    Task RemoveScanLocationAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ExistingFileSnapshot>> GetFileSnapshotsAsync(CancellationToken cancellationToken);
    Task TouchUnchangedFilesAsync(IReadOnlyList<long> fileIds, DateTimeOffset lastSeen, CancellationToken cancellationToken);
    Task<GameFile?> FindFileByPathAsync(string fullPath, CancellationToken cancellationToken);
    Task UpsertFileAsync(GameFile file, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameFile>> FindQuickHashMatchesAsync(string quickHash, long size, long excludingId, CancellationToken cancellationToken);
    Task SaveSha256Async(long gameFileId, string sha256, CancellationToken cancellationToken);
    Task MarkExactDuplicatesAsync(string sha256, CancellationToken cancellationToken);
    Task<long> MarkUnseenFilesMissingAsync(int locationId, DateTimeOffset scanStarted, CancellationToken cancellationToken);
    Task BeginScanAsync(int locationId, DateTimeOffset started, CancellationToken cancellationToken);
    Task CompleteScanAsync(int locationId, DateTimeOffset started, long fileCount, CancellationToken cancellationToken);
    Task<bool> HasInterruptedScanAsync(CancellationToken cancellationToken);
    Task<Game> GetOrCreateGameAsync(string title, string normalizedTitle, int systemId, CancellationToken cancellationToken);
    Task<int> ResolveFormatIdAsync(string systemKey, string extension, string formatName, CancellationToken cancellationToken);
    Task<long> GetOrCreateFileGroupAsync(long gameId, string displayName, int? discNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string? search, int? systemId, LibraryViewFilter filter, CancellationToken cancellationToken);
    Task<Game?> GetGameDetailsAsync(long id, CancellationToken cancellationToken);
    Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryExportRow>> GetLibraryExportRowsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<VerificationCandidate>> GetVerificationCandidatesAsync(string? systemKey, bool includeAlreadyChecked, CancellationToken cancellationToken);
    Task ApplyCatalogVerificationAsync(IReadOnlyList<CatalogVerificationUpdate> updates, CancellationToken cancellationToken);
    Task RecalculatePreferredCopiesAsync(string? systemKey, CancellationToken cancellationToken);
    Task SetCopyPreferenceAsync(long fileId, bool manuallyPreferred, bool excluded, CancellationToken cancellationToken);
    Task ExcludeNonPreferredCopiesAsync(IReadOnlyList<long> gameIds, CancellationToken cancellationToken);
    Task ClearCopyOverridesAsync(IReadOnlyList<long> gameIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<FuzzyMatchCandidate>> GetFuzzyMatchCandidatesAsync(CancellationToken cancellationToken);
    Task MergeGamesAsync(long keepGameId, long mergeGameId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PreferredExportFile>> GetPreferredExportFilesAsync(bool onlyWanted, CancellationToken cancellationToken);
    Task SetGamesWantedAsync(IReadOnlyList<long> gameIds, bool wanted, CancellationToken cancellationToken);
    Task<IReadOnlyList<TitleCleanupCandidate>> GetTitleCleanupCandidatesAsync(CancellationToken cancellationToken);
    Task ApplyTitleCleanupAsync(long gameId, string newTitle, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExactTitleDuplicateGroup>> GetExactTitleDuplicateGroupsAsync(CancellationToken cancellationToken);
    Task MergeExactTitleDuplicateGroupAsync(IReadOnlyList<long> gameIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<DuplicateFileRow>> GetDuplicateFileReportAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SystemDefinition>> GetSystemsAsync(CancellationToken cancellationToken);
}

public interface ISystemDefinitionProvider { Task<IReadOnlyList<SystemDefinition>> LoadAsync(CancellationToken cancellationToken); }
public interface IGameGroupingService { string CreateGroupingKey(ParsedFileName parsed, string systemKey); }
public interface ILibraryScanner
{
    Task<ScanResult> ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
    Task<ScanResult> ScanLocationAsync(ScanLocation location, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}
