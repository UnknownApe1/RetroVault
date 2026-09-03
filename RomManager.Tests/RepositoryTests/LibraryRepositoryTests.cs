using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RomManager.Core.Models;
using RomManager.Core.Services;
using RomManager.Database.Repositories;
using RomManager.Database.SQLite;

namespace RomManager.Tests.RepositoryTests;

public sealed class LibraryRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly LibraryRepository repository;
    private readonly DbContextOptions<RomManagerDbContext> options;

    public LibraryRepositoryTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<RomManagerDbContext>().UseSqlite(connection).Options;
        using (var db = new RomManagerDbContext(options)) db.Database.EnsureCreated();
        repository = new LibraryRepository(new TestDbContextFactory(options), new StubSystemDefinitionProvider());
    }

    public void Dispose() => connection.Dispose();

    private RomManagerDbContext CreateContext() => new(options);

    private async Task<(long GameId, long UsaFileId, long JapanFileId)> SeedTwoCopiesAsync(bool usaVerified = true)
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        var game = new Game { CanonicalTitle = "Test Game", SortTitle = "Test Game", NormalizedTitle = "testgame", SystemDefinitionId = system.Id };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var usaFile = new GameFile
        {
            GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Test Game (USA).nes", FileName = "Test Game (USA).nes", Extension = ".nes",
            Status = FileStatus.Normal, CatalogStatus = usaVerified ? CatalogVerificationStatus.Verified : CatalogVerificationStatus.Unknown, CatalogName = "Test Game (USA)"
        };
        var japanFile = new GameFile
        {
            GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Test Game (Japan).nes", FileName = "Test Game (Japan).nes", Extension = ".nes",
            Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.Unknown, CatalogName = "Test Game (Japan)"
        };
        db.GameFiles.AddRange(usaFile, japanFile);
        await db.SaveChangesAsync();
        return (game.Id, usaFile.Id, japanFile.Id);
    }

    [Fact]
    public async Task RecalculatePreferredCopiesAsync_PrefersVerifiedUsaOverUnverifiedJapan()
    {
        var (_, usaFileId, japanFileId) = await SeedTwoCopiesAsync();

        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        await using var db = CreateContext();
        Assert.True((await db.GameFiles.SingleAsync(x => x.Id == usaFileId)).IsPreferred);
        Assert.False((await db.GameFiles.SingleAsync(x => x.Id == japanFileId)).IsPreferred);
    }

    [Fact]
    public async Task RecalculatePreferredCopiesAsync_ExcludesBetaAndPrototypeCopies()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var game = new Game { CanonicalTitle = "Test Game", SortTitle = "Test Game", NormalizedTitle = "testgame", SystemDefinitionId = system.Id };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var betaFile = new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Test Game (Beta).nes", FileName = "Test Game (Beta).nes", Extension = ".nes", Status = FileStatus.Normal };
        var finalFile = new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Test Game (Europe).nes", FileName = "Test Game (Europe).nes", Extension = ".nes", Status = FileStatus.Normal };
        db.GameFiles.AddRange(betaFile, finalFile);
        await db.SaveChangesAsync();

        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        await using var verify = CreateContext();
        Assert.False((await verify.GameFiles.SingleAsync(x => x.Id == betaFile.Id)).IsPreferred);
        Assert.True((await verify.GameFiles.SingleAsync(x => x.Id == finalFile.Id)).IsPreferred);
    }

    [Fact]
    public async Task SetCopyPreferenceAsync_ManualChoiceOverridesAutomaticScoringAndClearsOtherCopies()
    {
        var (_, usaFileId, japanFileId) = await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        await repository.SetCopyPreferenceAsync(japanFileId, manuallyPreferred: true, excluded: false, CancellationToken.None);

        await using var db = CreateContext();
        var japan = await db.GameFiles.SingleAsync(x => x.Id == japanFileId);
        var usa = await db.GameFiles.SingleAsync(x => x.Id == usaFileId);
        Assert.True(japan.IsManuallyPreferred);
        Assert.True(japan.IsPreferred);
        Assert.False(usa.IsManuallyPreferred);
        Assert.False(usa.IsPreferred);
    }

    [Fact]
    public async Task SetCopyPreferenceAsync_ExcludingThePreferredCopyPromotesTheNextBest()
    {
        var (_, usaFileId, japanFileId) = await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        await repository.SetCopyPreferenceAsync(usaFileId, manuallyPreferred: false, excluded: true, CancellationToken.None);

        await using var db = CreateContext();
        Assert.False((await db.GameFiles.SingleAsync(x => x.Id == usaFileId)).IsPreferred);
        Assert.True((await db.GameFiles.SingleAsync(x => x.Id == japanFileId)).IsPreferred);
    }

    [Fact]
    public async Task SearchGamesAsync_VerifiedFilterOnlyReturnsGamesWithAVerifiedCopy()
    {
        await SeedTwoCopiesAsync(usaVerified: true);
        await using (var db = CreateContext())
        {
            var system = await db.Systems.SingleAsync();
            var unverifiedGame = new Game { CanonicalTitle = "Other Game", SortTitle = "Other Game", NormalizedTitle = "othergame", SystemDefinitionId = system.Id };
            db.Games.Add(unverifiedGame);
            await db.SaveChangesAsync();
            var location = await db.ScanLocations.SingleAsync();
            db.GameFiles.Add(new GameFile { GameId = unverifiedGame.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Other.nes", FileName = "Other.nes", Extension = ".nes", Status = FileStatus.Normal });
            await db.SaveChangesAsync();
        }

        var verifiedOnly = await repository.SearchGamesAsync(null, null, LibraryViewFilter.Verified, CancellationToken.None);

        var title = Assert.Single(verifiedOnly);
        Assert.Equal("Test Game", title.Title);
    }

    [Fact]
    public async Task SearchGamesAsync_NeedsReviewFilterReturnsNoMatchAndErrorCopies()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var game = new Game { CanonicalTitle = "Unmatched Game", SortTitle = "Unmatched Game", NormalizedTitle = "unmatchedgame", SystemDefinitionId = system.Id };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        db.GameFiles.Add(new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Unmatched.nes", FileName = "Unmatched.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.NoMatch });
        await db.SaveChangesAsync();

        var needsReview = await repository.SearchGamesAsync(null, null, LibraryViewFilter.NeedsReview, CancellationToken.None);

        Assert.Contains(needsReview, x => x.Title == "Unmatched Game");
    }

    [Fact]
    public async Task GetLibraryExportRowsAsync_IncludesCatalogAndPreferenceFields()
    {
        var (_, usaFileId, _) = await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        var rows = await repository.GetLibraryExportRowsAsync(CancellationToken.None);

        var usaRow = Assert.Single(rows, x => x.FullPath == "C:\\ROMs\\Test Game (USA).nes");
        Assert.Equal(CatalogVerificationStatus.Verified, usaRow.CatalogStatus);
        Assert.True(usaRow.IsPreferred);
        Assert.Equal("Test Game", usaRow.GameTitle);
        Assert.Equal("NES", usaRow.System);
    }

    [Fact]
    public async Task MergeGamesAsync_ReassignsFilesAndRemovesTheMergedGame()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var keep = new Game { CanonicalTitle = "Sonic the Hedgehog", SortTitle = "Sonic the Hedgehog", NormalizedTitle = "sonicthehedgehog", SystemDefinitionId = system.Id };
        var duplicate = new Game { CanonicalTitle = "Sonik the Hedgehog", SortTitle = "Sonik the Hedgehog", NormalizedTitle = "sonikthehedgehog", SystemDefinitionId = system.Id };
        db.Games.AddRange(keep, duplicate);
        await db.SaveChangesAsync();
        var duplicateFile = new GameFile { GameId = duplicate.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Sonik.nes", FileName = "Sonik.nes", Extension = ".nes", Status = FileStatus.Normal };
        db.GameFiles.Add(duplicateFile);
        await db.SaveChangesAsync();

        await repository.MergeGamesAsync(keep.Id, duplicate.Id, CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Equal(keep.Id, (await verify.GameFiles.SingleAsync(x => x.Id == duplicateFile.Id)).GameId);
        Assert.False(await verify.Games.AnyAsync(x => x.Id == duplicate.Id));
    }

    private sealed class TestDbContextFactory(DbContextOptions<RomManagerDbContext> options) : IDbContextFactory<RomManagerDbContext>
    {
        public RomManagerDbContext CreateDbContext() => new(options);
        public Task<RomManagerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RomManagerDbContext(options));
    }

    private sealed class StubSystemDefinitionProvider : ISystemDefinitionProvider
    {
        public Task<IReadOnlyList<SystemDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SystemDefinition>>([]);
    }
}
