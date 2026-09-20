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
    public async Task SearchGamesAsync_WantedFilterOnlyReturnsGamesMarkedWanted()
    {
        var (gameId, _, _) = await SeedTwoCopiesAsync();
        await using (var db = CreateContext())
        {
            var system = await db.Systems.SingleAsync();
            db.Games.Add(new Game { CanonicalTitle = "Not Wanted", SortTitle = "Not Wanted", NormalizedTitle = "notwanted", SystemDefinitionId = system.Id });
            await db.SaveChangesAsync();
        }

        await repository.SetGamesWantedAsync([gameId], true, CancellationToken.None);

        var wanted = await repository.SearchGamesAsync(null, null, LibraryViewFilter.Wanted, CancellationToken.None);

        var summary = Assert.Single(wanted);
        Assert.Equal("Test Game", summary.Title);
        Assert.True(summary.IsWanted);
    }

    [Fact]
    public async Task SearchGamesPageAsync_ReturnsPagesAndHasMoreFlag()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        for (var i = 0; i < 3; i++)
            db.Games.Add(new Game { CanonicalTitle = $"Game {i}", SortTitle = $"Game {i}", NormalizedTitle = $"game{i}", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var first = await repository.SearchGamesPageAsync(null, null, LibraryViewFilter.All, 0, 2, CancellationToken.None);
        var second = await repository.SearchGamesPageAsync(null, null, LibraryViewFilter.All, 2, 2, CancellationToken.None);

        Assert.Equal(2, first.Games.Count);
        Assert.True(first.HasMore);
        Assert.Single(second.Games);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task SearchGamesAsync_UnboundedCompatibilityPathReturnsEveryGame()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        for (var i = 0; i < 6; i++)
            db.Games.Add(new Game { CanonicalTitle = $"Game {i}", SortTitle = $"Game {i}", NormalizedTitle = $"game{i}", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var results = await repository.SearchGamesAsync(null, null, LibraryViewFilter.All, CancellationToken.None);

        Assert.Equal(6, results.Count);
    }

    [Fact]
    public async Task AssignFileToSystemAsync_ReassignsFileWithoutChangingPhysicalPath()
    {
        var (_, usaFileId, _) = await SeedTwoCopiesAsync();
        await using var db = CreateContext();
        var genesis = new SystemDefinition { Key = "GENESIS", Name = "Genesis", Manufacturer = "Sega" };
        genesis.Formats.Add(new SystemFormat { Extension = ".nes", FormatName = "NES ROM", FormatType = FileCategory.Rom, Priority = 10 });
        db.Systems.Add(genesis);
        await db.SaveChangesAsync();

        await repository.AssignFileToSystemAsync(usaFileId, "GENESIS", CancellationToken.None);

        await using var verify = CreateContext();
        var file = await verify.GameFiles.Include(x => x.Game).ThenInclude(x => x!.SystemDefinition).SingleAsync(x => x.Id == usaFileId);
        Assert.Equal("C:\\ROMs\\Test Game (USA).nes", file.FullPath);
        Assert.Equal("GENESIS", file.Game!.SystemDefinition!.Key);
        Assert.True(file.IsDetectionManual);
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
    public async Task SearchGamesAsync_NeedsReviewFilterReturnsLowConfidenceDetection()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var game = new Game { CanonicalTitle = "Ambiguous Game", SortTitle = "Ambiguous Game", NormalizedTitle = "ambiguousgame", SystemDefinitionId = system.Id };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        db.GameFiles.Add(new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Ambiguous.iso", FileName = "Ambiguous.iso", Extension = ".iso", Status = FileStatus.Normal, DetectionConfidence = 0.5, DetectionReason = "Ambiguous extension" });
        await db.SaveChangesAsync();

        var needsReview = await repository.SearchGamesAsync(null, null, LibraryViewFilter.NeedsReview, CancellationToken.None);

        Assert.Contains(needsReview, x => x.Title == "Ambiguous Game");
    }

    [Fact]
    public async Task SearchGamesAsync_IncludesSystemKeyAndPreferredCatalogNameForThumbnailLookup()
    {
        await SeedTwoCopiesAsync(usaVerified: true);
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        var results = await repository.SearchGamesAsync(null, null, LibraryViewFilter.All, CancellationToken.None);

        var summary = Assert.Single(results);
        Assert.Equal("NES", summary.SystemKey);
        Assert.Equal("Test Game (USA)", summary.PreferredCatalogName);
    }

    [Fact]
    public async Task SearchGamesAsync_TotalSizeBytesSumsAllNonMissingCopiesButExcludesMissingOnes()
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
        db.GameFiles.AddRange(
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\A.nes", FileName = "A.nes", Extension = ".nes", Size = 1000, Status = FileStatus.Normal },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\B.nes", FileName = "B.nes", Extension = ".nes", Size = 2000, Status = FileStatus.Normal },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\C.nes", FileName = "C.nes", Extension = ".nes", Size = 5000, Status = FileStatus.Missing });
        await db.SaveChangesAsync();

        var results = await repository.SearchGamesAsync(null, null, LibraryViewFilter.All, CancellationToken.None);

        Assert.Equal(3000, Assert.Single(results).TotalSizeBytes);
    }

    [Fact]
    public async Task GetPreferredExportFilesAsync_ReturnsOnlyThePreferredCopy()
    {
        await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        var files = await repository.GetPreferredExportFilesAsync(onlyWanted: false, CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal("NES", file.SystemName);
        Assert.Equal("Test Game (USA).nes", file.FileName);
    }

    [Fact]
    public async Task GetPreferredExportFilesAsync_WithOnlyWanted_ExcludesGamesNotMarkedWanted()
    {
        var (gameId, _, _) = await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        var beforeMarking = await repository.GetPreferredExportFilesAsync(onlyWanted: true, CancellationToken.None);
        await repository.SetGamesWantedAsync([gameId], true, CancellationToken.None);
        var afterMarking = await repository.GetPreferredExportFilesAsync(onlyWanted: true, CancellationToken.None);

        Assert.Empty(beforeMarking);
        Assert.Single(afterMarking);
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

    [Fact]
    public async Task ExcludeNonPreferredCopiesAsync_ExcludesEveryCopyExceptThePreferredOne()
    {
        var (gameId, usaFileId, japanFileId) = await SeedTwoCopiesAsync();
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);

        await repository.ExcludeNonPreferredCopiesAsync([gameId], CancellationToken.None);

        await using var db = CreateContext();
        var usa = await db.GameFiles.SingleAsync(x => x.Id == usaFileId);
        var japan = await db.GameFiles.SingleAsync(x => x.Id == japanFileId);
        Assert.True(usa.IsPreferred);
        Assert.False(usa.IsExcluded);
        Assert.False(japan.IsPreferred);
        Assert.True(japan.IsExcluded);
    }

    [Fact]
    public async Task ClearCopyOverridesAsync_ResetsManualPreferenceAndExclusion()
    {
        var (gameId, usaFileId, japanFileId) = await SeedTwoCopiesAsync();
        await repository.SetCopyPreferenceAsync(japanFileId, manuallyPreferred: true, excluded: false, CancellationToken.None);

        await repository.ClearCopyOverridesAsync([gameId], CancellationToken.None);

        await using var db = CreateContext();
        var usa = await db.GameFiles.SingleAsync(x => x.Id == usaFileId);
        var japan = await db.GameFiles.SingleAsync(x => x.Id == japanFileId);
        Assert.False(japan.IsManuallyPreferred);
        Assert.False(japan.IsExcluded);
        Assert.False(usa.IsManuallyPreferred);
        // Automatic scoring resumes: the verified USA copy outranks the unverified Japan copy again.
        Assert.True(usa.IsPreferred);
        Assert.False(japan.IsPreferred);
    }

    [Fact]
    public async Task GetTitleCleanupCandidatesAsync_SuggestsStrippingANumericRankPrefix()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        db.Games.Add(new Game { CanonicalTitle = "0002 Dragon Quest 1+2", SortTitle = "0002 Dragon Quest 1+2", NormalizedTitle = "0002dragonquest12", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var candidates = await repository.GetTitleCleanupCandidatesAsync(CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Dragon Quest 1+2", candidate.SuggestedTitle);
    }

    [Fact]
    public async Task GetTitleCleanupCandidatesAsync_PrefersTheVerifiedCatalogNameWhenOneExists()
    {
        var (gameId, usaFileId, _) = await SeedTwoCopiesAsync(usaVerified: true);
        await repository.RecalculatePreferredCopiesAsync(null, CancellationToken.None);
        await using (var db = CreateContext())
        {
            var game = await db.Games.SingleAsync(x => x.Id == gameId);
            game.CanonicalTitle = "0005 Test Game";
            await db.SaveChangesAsync();
        }

        var candidates = await repository.GetTitleCleanupCandidatesAsync(CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Test Game", candidate.SuggestedTitle);
        Assert.Equal("Matches verified catalog name", candidate.Reason);
    }

    [Fact]
    public async Task GetTitleCleanupCandidatesAsync_LeavesTitlesWithoutARankPrefixOrVerifiedNameAlone()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "NES", Name = "NES", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        db.Games.Add(new Game { CanonicalTitle = "1080 Snowboarding", SortTitle = "1080 Snowboarding", NormalizedTitle = "1080snowboarding", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var candidates = await repository.GetTitleCleanupCandidatesAsync(CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetTitleCleanupCandidatesAsync_StripsAnUnpaddedRankPrefixWhenTheWholeSystemSharesTheConvention()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "SNES", Name = "Super Nintendo", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        db.Games.AddRange(
            new Game { CanonicalTitle = "100 Bubsy in Claws Encounters of the Furred Kind", SortTitle = "100 Bubsy", NormalizedTitle = "100bubsy", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "101 Bugs Bunny in Rabbit Rampage", SortTitle = "101 Bugs Bunny", NormalizedTitle = "101bugsbunny", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "102 Captain America and the Avengers", SortTitle = "102 Captain America", NormalizedTitle = "102captainamerica", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "103 Captain Commando", SortTitle = "103 Captain Commando", NormalizedTitle = "103captaincommando", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "104 Captain Tsubasa 4", SortTitle = "104 Captain Tsubasa 4", NormalizedTitle = "104captaintsubasa4", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "Chrono Trigger", SortTitle = "Chrono Trigger", NormalizedTitle = "chronotrigger", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var candidates = await repository.GetTitleCleanupCandidatesAsync(CancellationToken.None);

        Assert.Equal(5, candidates.Count);
        Assert.Contains(candidates, x => x.CurrentTitle == "100 Bubsy in Claws Encounters of the Furred Kind" && x.SuggestedTitle == "Bubsy in Claws Encounters of the Furred Kind");
        Assert.DoesNotContain(candidates, x => x.CurrentTitle == "Chrono Trigger");
    }

    [Fact]
    public async Task ApplyTitleCleanupAsync_UpdatesTheCanonicalTitleButLeavesTheNormalizedTitleUnchanged()
    {
        var (gameId, _, _) = await SeedTwoCopiesAsync();
        string normalizedTitleBefore;
        await using (var db = CreateContext()) normalizedTitleBefore = (await db.Games.SingleAsync(x => x.Id == gameId)).NormalizedTitle;

        await repository.ApplyTitleCleanupAsync(gameId, "Test Game (Cleaned)", CancellationToken.None);

        await using var verify = CreateContext();
        var game = await verify.Games.SingleAsync(x => x.Id == gameId);
        Assert.Equal("Test Game (Cleaned)", game.CanonicalTitle);
        Assert.Equal(normalizedTitleBefore, game.NormalizedTitle);
    }

    [Fact]
    public async Task GetPreferredExportFilesAsync_AndGetVerificationCandidatesAsync_SurfaceTheIsDirectoryFlag()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "PS3", Name = "PlayStation 3", Manufacturer = "Sony" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "D:\\Games\\Roms\\PS3_Games" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var game = new Game { CanonicalTitle = "Army of Two", SortTitle = "Army of Two", NormalizedTitle = "armyoftwo", SystemDefinitionId = system.Id };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        db.GameFiles.Add(new GameFile
        {
            GameId = game.Id, ScanLocationId = location.Id, FullPath = "D:\\Games\\Roms\\PS3_Games\\Army of Two", FileName = "Army of Two", Extension = ".ps3dir",
            Status = FileStatus.Normal, IsDirectory = true, IsPreferred = true
        });
        await db.SaveChangesAsync();

        var exportFiles = await repository.GetPreferredExportFilesAsync(onlyWanted: false, CancellationToken.None);
        var verificationCandidates = await repository.GetVerificationCandidatesAsync(null, includeAlreadyChecked: true, CancellationToken.None);

        Assert.True(Assert.Single(exportFiles).IsDirectory);
        Assert.True(Assert.Single(verificationCandidates).IsDirectory);
    }

    [Fact]
    public async Task GetDuplicateFileReportAsync_GroupsDuplicateStatusFilesBySha256()
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
        var dupeA = new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\A.nes", FileName = "A.nes", Extension = ".nes", Size = 1000, Status = FileStatus.Duplicate };
        var dupeB = new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\B.nes", FileName = "B.nes", Extension = ".nes", Size = 1000, Status = FileStatus.Duplicate };
        var unique = new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\C.nes", FileName = "C.nes", Extension = ".nes", Size = 500, Status = FileStatus.Normal };
        db.GameFiles.AddRange(dupeA, dupeB, unique);
        await db.SaveChangesAsync();
        db.Hashes.AddRange(
            new FileHash { GameFileId = dupeA.Id, Algorithm = "SHA256", Hash = "same-hash" },
            new FileHash { GameFileId = dupeB.Id, Algorithm = "SHA256", Hash = "same-hash" },
            new FileHash { GameFileId = unique.Id, Algorithm = "SHA256", Hash = "other-hash" });
        await db.SaveChangesAsync();

        var report = await repository.GetDuplicateFileReportAsync(CancellationToken.None);

        Assert.Equal(2, report.Count);
        Assert.All(report, x => Assert.Equal("same-hash", x.Sha256));
        Assert.Equal(["A.nes", "B.nes"], report.Select(x => x.FileName).OrderBy(x => x));
    }

    [Fact]
    public async Task GetVerificationCandidatesAsync_DefaultSkipsFilesAlreadyVerifiedOrNoMatchOrUnsupportedButRetriesErrors()
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
        db.GameFiles.AddRange(
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Unknown.nes", FileName = "Unknown.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.Unknown },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Verified.nes", FileName = "Verified.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.Verified },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\NoMatch.nes", FileName = "NoMatch.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.NoMatch },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Unsupported.nes", FileName = "Unsupported.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.Unsupported },
            new GameFile { GameId = game.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Errored.nes", FileName = "Errored.nes", Extension = ".nes", Status = FileStatus.Normal, CatalogStatus = CatalogVerificationStatus.Error });
        await db.SaveChangesAsync();

        var incremental = await repository.GetVerificationCandidatesAsync(null, includeAlreadyChecked: false, CancellationToken.None);
        var full = await repository.GetVerificationCandidatesAsync(null, includeAlreadyChecked: true, CancellationToken.None);

        Assert.Equal(["Errored.nes", "Unknown.nes"], incremental.Select(x => x.FileName).OrderBy(x => x));
        Assert.Equal(5, full.Count);
    }

    [Fact]
    public async Task GetExactTitleDuplicateGroupsAsync_GroupsGamesSharingASystemAndExactCanonicalTitle()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "SNES", Name = "Super Nintendo", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        await db.SaveChangesAsync();
        db.Games.AddRange(
            new Game { CanonicalTitle = "Bubsy", SortTitle = "Bubsy", NormalizedTitle = "bubsy1", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "Bubsy", SortTitle = "Bubsy", NormalizedTitle = "bubsy2", SystemDefinitionId = system.Id },
            new Game { CanonicalTitle = "Chrono Trigger", SortTitle = "Chrono Trigger", NormalizedTitle = "chronotrigger", SystemDefinitionId = system.Id });
        await db.SaveChangesAsync();

        var groups = await repository.GetExactTitleDuplicateGroupsAsync(CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal("Bubsy", group.Title);
        Assert.Equal("Super Nintendo", group.SystemName);
        Assert.Equal(2, group.GameIds.Count);
    }

    [Fact]
    public async Task MergeExactTitleDuplicateGroupAsync_KeepsTheCopyWithTheMostFilesAndReassignsTheRest()
    {
        await using var db = CreateContext();
        var system = new SystemDefinition { Key = "SNES", Name = "Super Nintendo", Manufacturer = "Nintendo" };
        db.Systems.Add(system);
        var location = new ScanLocation { Path = "C:\\ROMs" };
        db.ScanLocations.Add(location);
        await db.SaveChangesAsync();
        var smallCopy = new Game { CanonicalTitle = "Bubsy", SortTitle = "Bubsy", NormalizedTitle = "bubsy1", SystemDefinitionId = system.Id };
        var bigCopy = new Game { CanonicalTitle = "Bubsy", SortTitle = "Bubsy", NormalizedTitle = "bubsy2", SystemDefinitionId = system.Id };
        db.Games.AddRange(smallCopy, bigCopy);
        await db.SaveChangesAsync();
        db.GameFiles.Add(new GameFile { GameId = smallCopy.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Bubsy1.smc", FileName = "Bubsy1.smc", Extension = ".smc", Status = FileStatus.Normal });
        db.GameFiles.AddRange(
            new GameFile { GameId = bigCopy.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Bubsy2.smc", FileName = "Bubsy2.smc", Extension = ".smc", Status = FileStatus.Normal },
            new GameFile { GameId = bigCopy.Id, ScanLocationId = location.Id, FullPath = "C:\\ROMs\\Bubsy2 (Alt).smc", FileName = "Bubsy2 (Alt).smc", Extension = ".smc", Status = FileStatus.Normal });
        await db.SaveChangesAsync();

        await repository.MergeExactTitleDuplicateGroupAsync([smallCopy.Id, bigCopy.Id], CancellationToken.None);

        await using var verify = CreateContext();
        Assert.False(await verify.Games.AnyAsync(x => x.Id == smallCopy.Id));
        Assert.True(await verify.Games.AnyAsync(x => x.Id == bigCopy.Id));
        Assert.Equal(3, await verify.GameFiles.CountAsync(x => x.GameId == bigCopy.Id));
    }

    [Fact]
    public async Task GetGameCountsBySystemAsync_CountsDistinctGamesNotFilesSoDuplicateCopiesDoNotInflateIt()
    {
        // SeedTwoCopiesAsync creates ONE game with TWO files (a USA and a Japan copy); the count must
        // reflect the one game, not the two file rows.
        await SeedTwoCopiesAsync();
        int systemId;
        await using (var db = CreateContext())
        {
            var system = await db.Systems.SingleAsync();
            systemId = system.Id;
            db.Games.Add(new Game { CanonicalTitle = "Second Game", SortTitle = "Second Game", NormalizedTitle = "secondgame", SystemDefinitionId = system.Id });
            await db.SaveChangesAsync();
        }

        var counts = await repository.GetGameCountsBySystemAsync(CancellationToken.None);

        Assert.Equal(2, counts[systemId]);
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
