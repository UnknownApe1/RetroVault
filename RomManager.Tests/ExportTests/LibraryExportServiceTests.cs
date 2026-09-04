using Microsoft.Extensions.Logging.Abstractions;
using RomManager.Core.Models;
using RomManager.Core.Services;
using RomManager.Infrastructure.Export;

namespace RomManager.Tests.ExportTests;

public sealed class LibraryExportServiceTests : IDisposable
{
    private readonly string sourceRoot;
    private readonly string destinationRoot;

    public LibraryExportServiceTests()
    {
        sourceRoot = Path.Combine(Path.GetTempPath(), $"rom-manager-export-src-{Guid.NewGuid():N}");
        destinationRoot = Path.Combine(Path.GetTempPath(), $"rom-manager-export-dst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
        if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true);
    }

    [Fact]
    public async Task ExportPreferredCopiesAsync_CopiesEachFileIntoAPerSystemSubfolder()
    {
        var path = Path.Combine(sourceRoot, "Game.nes");
        await File.WriteAllTextAsync(path, "rom bytes");
        var repository = new StubRepository([new PreferredExportFile("Nintendo Entertainment System", path, "Game.nes")]);
        var service = new LibraryExportService(repository, NullLogger<LibraryExportService>.Instance);

        var result = await service.ExportPreferredCopiesAsync(destinationRoot, false, null, CancellationToken.None);

        Assert.Equal(1, result.Copied);
        Assert.Equal(0, result.Skipped);
        var destinationPath = Path.Combine(destinationRoot, "Nintendo Entertainment System", "Game.nes");
        Assert.True(File.Exists(destinationPath));
        Assert.Equal("rom bytes", await File.ReadAllTextAsync(destinationPath));
    }

    [Fact]
    public async Task ExportPreferredCopiesAsync_SkipsAFileAlreadyExportedWithMatchingSize()
    {
        var path = Path.Combine(sourceRoot, "Game.nes");
        await File.WriteAllTextAsync(path, "rom bytes");
        var systemFolder = Path.Combine(destinationRoot, "Nintendo Entertainment System");
        Directory.CreateDirectory(systemFolder);
        await File.WriteAllTextAsync(Path.Combine(systemFolder, "Game.nes"), "rom bytes");
        var repository = new StubRepository([new PreferredExportFile("Nintendo Entertainment System", path, "Game.nes")]);
        var service = new LibraryExportService(repository, NullLogger<LibraryExportService>.Instance);

        var result = await service.ExportPreferredCopiesAsync(destinationRoot, false, null, CancellationToken.None);

        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ExportPreferredCopiesAsync_SanitizesInvalidCharactersInTheSystemFolderName()
    {
        var path = Path.Combine(sourceRoot, "Game.dat");
        await File.WriteAllTextAsync(path, "x");
        var repository = new StubRepository([new PreferredExportFile("Sys:Tem*Name", path, "Game.dat")]);
        var service = new LibraryExportService(repository, NullLogger<LibraryExportService>.Instance);

        await service.ExportPreferredCopiesAsync(destinationRoot, false, null, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(destinationRoot, "Sys_Tem_Name")));
    }

    private sealed class StubRepository(IReadOnlyList<PreferredExportFile> files) : ILibraryRepository
    {
        public Task<IReadOnlyList<PreferredExportFile>> GetPreferredExportFilesAsync(bool onlyWanted, CancellationToken ct) => Task.FromResult(files);
        public Task SetGamesWantedAsync(IReadOnlyList<long> gameIds, bool wanted, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TitleCleanupCandidate>> GetTitleCleanupCandidatesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task ApplyTitleCleanupAsync(long gameId, string newTitle, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExactTitleDuplicateGroup>> GetExactTitleDuplicateGroupsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task MergeExactTitleDuplicateGroupAsync(IReadOnlyList<long> gameIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DuplicateFileRow>> GetDuplicateFileReportAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task InitializeAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ScanLocation>> GetScanLocationsAsync(bool enabledOnly, CancellationToken ct) => throw new NotSupportedException();
        public Task<ScanLocation> AddScanLocationAsync(string path, bool recursive, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateScanLocationAsync(int id, bool enabled, bool recursive, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveScanLocationAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, ExistingFileSnapshot>> GetFileSnapshotsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task TouchUnchangedFilesAsync(IReadOnlyList<long> fileIds, DateTimeOffset lastSeen, CancellationToken ct) => throw new NotSupportedException();
        public Task<GameFile?> FindFileByPathAsync(string fullPath, CancellationToken ct) => throw new NotSupportedException();
        public Task UpsertFileAsync(GameFile file, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameFile>> FindQuickHashMatchesAsync(string quickHash, long size, long excludingId, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSha256Async(long gameFileId, string sha256, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkExactDuplicatesAsync(string sha256, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> MarkUnseenFilesMissingAsync(int locationId, DateTimeOffset scanStarted, CancellationToken ct) => throw new NotSupportedException();
        public Task BeginScanAsync(int locationId, DateTimeOffset started, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteScanAsync(int locationId, DateTimeOffset started, long fileCount, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasInterruptedScanAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Game> GetOrCreateGameAsync(string title, string normalizedTitle, int systemId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> ResolveFormatIdAsync(string systemKey, string extension, string formatName, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> GetOrCreateFileGroupAsync(long gameId, string displayName, int? discNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string? search, int? systemId, LibraryViewFilter filter, CancellationToken ct) => throw new NotSupportedException();
        public Task<Game?> GetGameDetailsAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<LibraryCounts> GetCountsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryExportRow>> GetLibraryExportRowsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<VerificationCandidate>> GetVerificationCandidatesAsync(string? systemKey, bool includeAlreadyChecked, CancellationToken ct) => throw new NotSupportedException();
        public Task ApplyCatalogVerificationAsync(IReadOnlyList<CatalogVerificationUpdate> updates, CancellationToken ct) => throw new NotSupportedException();
        public Task RecalculatePreferredCopiesAsync(string? systemKey, CancellationToken ct) => throw new NotSupportedException();
        public Task SetCopyPreferenceAsync(long fileId, bool manuallyPreferred, bool excluded, CancellationToken ct) => throw new NotSupportedException();
        public Task ExcludeNonPreferredCopiesAsync(IReadOnlyList<long> gameIds, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearCopyOverridesAsync(IReadOnlyList<long> gameIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<FuzzyMatchCandidate>> GetFuzzyMatchCandidatesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task MergeGamesAsync(long keepGameId, long mergeGameId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SystemDefinition>> GetSystemsAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
