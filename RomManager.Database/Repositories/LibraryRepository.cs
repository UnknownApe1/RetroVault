using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RomManager.Core.Grouping;
using RomManager.Core.Models;
using RomManager.Core.Services;
using RomManager.Database.SQLite;
using RomManager.Database.Migrations;

namespace RomManager.Database.Repositories;

public sealed class LibraryRepository(IDbContextFactory<RomManagerDbContext> factory, ISystemDefinitionProvider definitions) : ILibraryRepository, IPagedLibraryRepository, IManualFileAssignmentRepository
{
    private readonly SemaphoreSlim gameCacheGate = new(1, 1);
    private Dictionary<int, int> sourceSystemDatabaseIds = [];
    private Dictionary<(string SystemKey, string Extension, string FormatName), int> formatDatabaseIds = [];
    private Dictionary<(int SourceSystemId, string NormalizedTitle), Game>? gamesByKey;

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await SchemaMigrator.ApplyAsync(db, ct);
        var source = await definitions.LoadAsync(ct);
        var existingSystems = await db.Systems.Include(x => x.Formats).ToDictionaryAsync(x => x.Key, ct);
        foreach (var definition in source)
        {
            if (!existingSystems.TryGetValue(definition.Key, out var system))
            {
                db.Systems.Add(new SystemDefinition { Key = definition.Key, Name = definition.Name, Manufacturer = definition.Manufacturer, Category = definition.Category, Enabled = definition.Enabled,
                    Formats = definition.Formats.Select(f => new SystemFormat { Extension = f.Extension, FormatName = f.FormatName, FormatType = f.FormatType, Priority = f.Priority, RequiresHeaderCheck = f.RequiresHeaderCheck, IsArchive = f.IsArchive, IsMultiFile = f.IsMultiFile }).ToList() });
            }
            else
            {
                system.Name = definition.Name; system.Manufacturer = definition.Manufacturer; system.Category = definition.Category;
                foreach (var sourceFormat in definition.Formats)
                {
                    var format = system.Formats.SingleOrDefault(x => x.Extension == sourceFormat.Extension && x.FormatName == sourceFormat.FormatName);
                    if (format is null) system.Formats.Add(new SystemFormat { Extension = sourceFormat.Extension, FormatName = sourceFormat.FormatName, FormatType = sourceFormat.FormatType, Priority = sourceFormat.Priority, RequiresHeaderCheck = sourceFormat.RequiresHeaderCheck, IsArchive = sourceFormat.IsArchive, IsMultiFile = sourceFormat.IsMultiFile });
                    else { format.FormatType = sourceFormat.FormatType; format.Priority = sourceFormat.Priority; format.RequiresHeaderCheck = sourceFormat.RequiresHeaderCheck; format.IsArchive = sourceFormat.IsArchive; format.IsMultiFile = sourceFormat.IsMultiFile; }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        var databaseSystems = await db.Systems.AsNoTracking().ToDictionaryAsync(x => x.Key, ct);
        sourceSystemDatabaseIds = source.ToDictionary(x => x.Id, x => databaseSystems[x.Key].Id);
        formatDatabaseIds = await db.SystemFormats.AsNoTracking().Select(x => new
        {
            SystemKey = x.SystemDefinition!.Key,
            x.Extension,
            x.FormatName,
            x.Id
        }).ToDictionaryAsync(x => new ValueTuple<string, string, string>(x.SystemKey, x.Extension, x.FormatName), x => x.Id, ct);
        gamesByKey = null;
    }

    public async Task<IReadOnlyList<ScanLocation>> GetScanLocationsAsync(bool enabledOnly, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var q = db.ScanLocations.AsNoTracking(); return await (enabledOnly ? q.Where(x => x.Enabled) : q).OrderBy(x => x.Path).ToListAsync(ct); }

    public async Task<ScanLocation> AddScanLocationAsync(string path, bool recursive, CancellationToken ct)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.ScanLocations.SingleOrDefaultAsync(x => x.Path == path, ct);
        if (existing is not null) return existing;
        var item = new ScanLocation { Path = path, Recursive = recursive };
        db.Add(item); await db.SaveChangesAsync(ct); return item;
    }

    public async Task UpdateScanLocationAsync(int id, bool enabled, bool recursive, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.ScanLocations.FindAsync([id], ct); if (item is null) return; item.Enabled = enabled; item.Recursive = recursive; await db.SaveChangesAsync(ct); }

    public async Task RemoveScanLocationAsync(int id, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.ScanLocations.FindAsync([id], ct); if (item is not null) { db.Remove(item); await db.SaveChangesAsync(ct); await db.Games.Where(x => !x.Files.Any()).ExecuteDeleteAsync(ct); gamesByKey = null; } }

    public async Task<IReadOnlyDictionary<string, ExistingFileSnapshot>> GetFileSnapshotsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.GameFiles.AsNoTracking()
            .Select(x => new ExistingFileSnapshot(x.Id, x.FullPath, x.Size, x.ModifiedDate, x.Status)).ToListAsync(ct);
        var snapshots = new Dictionary<string, ExistingFileSnapshot>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) snapshots[row.FullPath] = row;
        return snapshots;
    }

    public async Task TouchUnchangedFilesAsync(IReadOnlyList<long> fileIds, DateTimeOffset lastSeen, CancellationToken ct)
    {
        if (fileIds.Count == 0) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            var idParameters = new string[fileIds.Count];
            AddParameter(command, "$lastSeen", lastSeen);
            AddParameter(command, "$missing", FileStatus.Missing.ToString());
            AddParameter(command, "$normal", FileStatus.Normal.ToString());
            for (var i = 0; i < fileIds.Count; i++)
            {
                idParameters[i] = $"$id{i}";
                AddParameter(command, idParameters[i], fileIds[i]);
            }
            command.CommandText = $"UPDATE GameFiles SET LastSeen = $lastSeen, Status = CASE WHEN Status = $missing THEN $normal ELSE Status END WHERE Id IN ({string.Join(",", idParameters)});";
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async Task<GameFile?> FindFileByPathAsync(string path, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.GameFiles.SingleOrDefaultAsync(x => x.FullPath == path, ct); }

    public async Task UpsertFileAsync(GameFile file, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (file.Id == 0) db.GameFiles.Add(file);
        else
        {
            if (file.CatalogStatus == CatalogVerificationStatus.Unknown) await db.Hashes.Where(x => x.GameFileId == file.Id).ExecuteDeleteAsync(ct);
            db.GameFiles.Update(file);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GameFile>> FindQuickHashMatchesAsync(string quickHash, long size, long excludingId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.GameFiles.AsNoTracking().Include(x => x.Hashes).Where(x => x.Id != excludingId && x.QuickHash == quickHash && x.Size == size && x.Status != FileStatus.Missing).ToListAsync(ct); }

    public async Task SaveSha256Async(long gameFileId, string sha256, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Hashes.SingleOrDefaultAsync(x => x.GameFileId == gameFileId && x.Algorithm == "SHA256", ct);
        if (existing is null) db.Hashes.Add(new FileHash { GameFileId = gameFileId, Algorithm = "SHA256", Hash = sha256 }); else { existing.Hash = sha256; existing.CalculatedDate = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkExactDuplicatesAsync(string sha256, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = await db.Hashes.Where(x => x.Algorithm == "SHA256" && x.Hash == sha256).Select(x => x.GameFileId).ToListAsync(ct);
        if (ids.Count > 1) await db.GameFiles.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FileStatus.Duplicate), ct);
    }

    public async Task<long> MarkUnseenFilesMissingAsync(int locationId, DateTimeOffset scanStarted, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // EF Core cannot reliably translate an enum comparison inside ExecuteUpdate
        // when the enum is stored as text. Use a parameterized SQLite update instead.
        var missingStatus = FileStatus.Missing.ToString();
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE GameFiles
            SET Status = {missingStatus}
            WHERE ScanLocationId = {locationId}
              AND LastSeen < {scanStarted}
              AND Status <> {missingStatus};
            """, ct);
    }

    public async Task BeginScanAsync(int locationId, DateTimeOffset started, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.ScanLocations.FindAsync([locationId], ct);
        if (item is null) return;
        item.LastScanStarted = started;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteScanAsync(int locationId, DateTimeOffset started, long fileCount, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var item = await db.ScanLocations.FindAsync([locationId], ct);
        if (item is null) return; item.LastScanStarted = started; item.LastScanCompleted = DateTimeOffset.UtcNow; item.FileCount = fileCount; await db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasInterruptedScanAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // SQLite stores DateTimeOffset values as text, so EF cannot translate a
        // comparison between these nullable columns. Only scan-location timestamps
        // are loaded; the comparison then runs safely in normal C#.
        var states = await db.ScanLocations.AsNoTracking()
            .Where(x => x.Enabled)
            .Select(x => new { x.LastScanStarted, x.LastScanCompleted })
            .ToListAsync(ct);
        return states.Any(x => x.LastScanStarted.HasValue &&
            (!x.LastScanCompleted.HasValue || x.LastScanStarted.Value > x.LastScanCompleted.Value));
    }

    public async Task<Game> GetOrCreateGameAsync(string title, string normalizedTitle, int sourceSystemId, CancellationToken ct)
    {
        await EnsureGameCacheAsync(ct);
        var key = (sourceSystemId, normalizedTitle);
        if (gamesByKey!.TryGetValue(key, out var cached)) return cached;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!sourceSystemDatabaseIds.TryGetValue(sourceSystemId, out var systemId)) throw new InvalidOperationException($"System {sourceSystemId} is not synchronized with the database.");
        var game = new Game { CanonicalTitle = title, SortTitle = CreateSortTitle(title), NormalizedTitle = normalizedTitle, SystemDefinitionId = systemId };
        db.Games.Add(game); await db.SaveChangesAsync(ct); gamesByKey[key] = game; return game;
    }

    public async Task<int> ResolveFormatIdAsync(string systemKey, string extension, string formatName, CancellationToken ct)
    {
        if (formatDatabaseIds.TryGetValue((systemKey, extension, formatName), out var cached)) return cached;
        await using var db = await factory.CreateDbContextAsync(ct);
        var id = await db.SystemFormats.Where(x => x.SystemDefinition!.Key == systemKey && x.Extension == extension && x.FormatName == formatName).Select(x => x.Id).SingleAsync(ct);
        formatDatabaseIds[(systemKey, extension, formatName)] = id;
        return id;
    }

    public async Task<long> GetOrCreateFileGroupAsync(long gameId, string displayName, int? discNumber, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var group = await db.FileGroups.SingleOrDefaultAsync(x => x.GameId == gameId && x.DiscNumber == discNumber && x.DisplayName == displayName, ct);
        if (group is not null) return group.Id;
        group = new FileGroup { GameId = gameId, GroupType = "MULTI_FILE_DISC", DisplayName = displayName, DiscNumber = discNumber };
        db.FileGroups.Add(group); await db.SaveChangesAsync(ct); return group.Id;
    }

    public async Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string? search, int? systemId, LibraryViewFilter filter, CancellationToken ct)
    {
        var result = await SearchGamesPageAsync(search, systemId, filter, 0, int.MaxValue, ct);
        return result.Games;
    }

    public async Task<PagedGameResult> SearchGamesPageAsync(string? search, int? systemId, LibraryViewFilter filter, int skip, int take, CancellationToken ct)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Games.AsNoTracking().AsQueryable();
        if (systemId.HasValue) q = q.Where(x => x.SystemDefinitionId == systemId);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); q = q.Where(x => EF.Functions.Like(x.CanonicalTitle, $"%{term}%") || x.Files.Any(f => EF.Functions.Like(f.FileName, $"%{term}%") || EF.Functions.Like(f.FullPath, $"%{term}%"))); }
        q = filter switch
        {
            LibraryViewFilter.Verified => q.Where(x => x.Files.Any(f => f.CatalogStatus == CatalogVerificationStatus.Verified)),
            LibraryViewFilter.Unverified => q.Where(x => x.Files.Any(f => f.Status != FileStatus.Missing && f.CatalogStatus != CatalogVerificationStatus.Verified)),
            LibraryViewFilter.ExactDuplicates => q.Where(x => x.Files.Any(f => f.Status == FileStatus.Duplicate)),
            LibraryViewFilter.MultipleVersions => q.Where(x => x.Files.Count(f => f.Status != FileStatus.Missing) > 1),
            LibraryViewFilter.Missing => q.Where(x => x.Files.Any(f => f.Status == FileStatus.Missing)),
            LibraryViewFilter.NeedsReview => q.Where(x => x.Files.Any(f => f.DetectionConfidence < 0.75 || f.CatalogStatus == CatalogVerificationStatus.NoMatch || f.CatalogStatus == CatalogVerificationStatus.Error || f.CatalogStatus == CatalogVerificationStatus.Unsupported)),
            LibraryViewFilter.PreferredCopies => q.Where(x => x.Files.Any(f => f.IsPreferred)),
            LibraryViewFilter.Wanted => q.Where(x => x.IsWanted),
            _ => q
        };
        var pageTake = take == int.MaxValue ? take : take + 1;
        var rows = await q.OrderBy(x => x.SortTitle).Skip(skip).Take(pageTake).Select(x => new
        {
            x.Id,
            Title = x.CanonicalTitle,
            System = x.SystemDefinition!.Name,
            SystemKey = x.SystemDefinition!.Key,
            FileCount = x.Files.Count,
            DuplicateCount = x.Files.Count(f => f.Status == FileStatus.Duplicate),
            VerifiedCount = x.Files.Count(f => f.CatalogStatus == CatalogVerificationStatus.Verified),
            MissingCount = x.Files.Count(f => f.Status == FileStatus.Missing),
            PreferredCatalogName = x.Files.Where(f => f.IsPreferred).Select(f => f.CatalogName).FirstOrDefault(),
            PreferredRegion = x.Files.OrderByDescending(f => f.IsPreferred).Select(f => f.Region).FirstOrDefault(),
            TotalSizeBytes = x.Files.Where(f => f.Status != FileStatus.Missing).Sum(f => (long?)f.Size) ?? 0,
            x.IsWanted
        }).ToListAsync(ct);
        var games = rows.Take(take).Select(x => new GameSummary(x.Id, x.Title, x.System, x.SystemKey, x.FileCount, x.DuplicateCount, x.VerifiedCount,
            x.MissingCount > 0 ? FileStatus.Missing : x.DuplicateCount > 0 ? FileStatus.Duplicate : FileStatus.Normal, x.PreferredCatalogName, x.PreferredRegion, x.TotalSizeBytes, x.IsWanted)).ToList();
        return new PagedGameResult(games, take != int.MaxValue && rows.Count > take);
    }

    public async Task<Game?> GetGameDetailsAsync(long id, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Games.AsNoTracking().Include(x => x.SystemDefinition).Include(x => x.Files).ThenInclude(x => x.SystemFormat).SingleOrDefaultAsync(x => x.Id == id, ct); }

    public async Task AssignFileToSystemAsync(long fileId, string systemKey, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var file = await db.GameFiles.Include(x => x.Game).SingleOrDefaultAsync(x => x.Id == fileId, ct)
            ?? throw new InvalidOperationException("The selected file is no longer in the library.");
        var targetSystem = await db.Systems.Include(x => x.Formats).SingleOrDefaultAsync(x => x.Key == systemKey, ct)
            ?? throw new InvalidOperationException($"System '{systemKey}' was not found.");
        var format = targetSystem.Formats
            .Where(x => x.Extension == file.Extension)
            .OrderByDescending(x => x.Priority)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{targetSystem.Name} does not define a format for {file.Extension}.");
        if (file.Game is null) throw new InvalidOperationException("The selected file has no game record to reassign.");
        var targetGame = await db.Games.SingleOrDefaultAsync(x => x.SystemDefinitionId == targetSystem.Id && x.NormalizedTitle == file.Game.NormalizedTitle, ct);
        if (targetGame is null)
        {
            targetGame = new Game
            {
                CanonicalTitle = file.Game.CanonicalTitle,
                SortTitle = file.Game.SortTitle,
                NormalizedTitle = file.Game.NormalizedTitle,
                SystemDefinitionId = targetSystem.Id,
                IsWanted = file.Game.IsWanted
            };
            db.Games.Add(targetGame);
            await db.SaveChangesAsync(ct);
        }
        file.GameId = targetGame.Id;
        file.SystemFormatId = format.Id;
        file.DetectionConfidence = 1;
        file.DetectionReason = $"Manually assigned to {targetSystem.Name}";
        file.IsDetectionManual = true;
        await db.SaveChangesAsync(ct);
        await db.GameFiles.Where(x => x.GameId == file.GameId && x.Id != file.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPreferred, false), ct);
        await transaction.CommitAsync(ct);
        gamesByKey = null;
    }

    public async Task<LibraryCounts> GetCountsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return new(await db.Games.LongCountAsync(ct), await db.GameFiles.LongCountAsync(ct), await db.GameFiles.LongCountAsync(x => x.Status == FileStatus.Duplicate, ct), await db.GameFiles.LongCountAsync(x => x.Status == FileStatus.Missing, ct), await db.GameFiles.LongCountAsync(x => x.CatalogStatus == CatalogVerificationStatus.Verified, ct)); }

    public async Task<IReadOnlyList<LibraryExportRow>> GetLibraryExportRowsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.GameFiles.AsNoTracking()
            .OrderBy(x => x.Game!.SystemDefinition!.Name).ThenBy(x => x.Game!.SortTitle).ThenBy(x => x.FileName)
            .Select(x => new LibraryExportRow(
                x.Game == null ? "" : x.Game.SystemDefinition!.Name,
                x.Game == null ? "" : x.Game.CanonicalTitle,
                x.FileName,
                x.FullPath,
                x.ScanLocation!.Path,
                x.SystemFormat == null ? "" : x.SystemFormat.FormatName,
                x.Size,
                x.ModifiedDate,
                x.Region,
                x.Language,
                x.Revision,
                x.Version,
                x.Status,
                x.CatalogStatus,
                x.CatalogSource,
                x.CatalogName,
                x.IsPreferred,
                x.IsManuallyPreferred,
                x.IsExcluded,
                x.PreferenceScore,
                x.QuickHash,
                x.Hashes.Where(h => h.Algorithm == "SHA256").Select(h => h.Hash).FirstOrDefault(),
                x.Hashes.Where(h => h.Algorithm == "SHA1").Select(h => h.Hash).FirstOrDefault())).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VerificationCandidate>> GetVerificationCandidatesAsync(string? systemKey, bool includeAlreadyChecked, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.GameFiles.AsNoTracking().Where(x => x.Game != null && x.Status != FileStatus.Missing);
        if (!string.IsNullOrWhiteSpace(systemKey)) query = query.Where(x => x.Game!.SystemDefinition!.Key == systemKey);
        // Verified/NoMatch/Unsupported are stable outcomes that don't change unless the catalog itself changes,
        // so a default run skips them and only spends time hashing files that have never been checked (or
        // previously errored, which is worth retrying). This is what makes repeated/incremental runs cheap -
        // without it, re-running "Verify Catalog" always re-hashes the entire library from scratch.
        if (!includeAlreadyChecked) query = query.Where(x => x.CatalogStatus == CatalogVerificationStatus.Unknown || x.CatalogStatus == CatalogVerificationStatus.Error);
        return await query.OrderBy(x => x.Game!.SystemDefinition!.Key).ThenBy(x => x.GameId).ThenBy(x => x.FullPath)
            .Select(x => new VerificationCandidate(x.Id, x.GameId!.Value, x.Game!.SystemDefinition!.Key, x.FullPath, x.FileName, x.Extension, x.Size, x.Status, x.Region, x.Revision, x.IsDirectory))
            .ToListAsync(ct);
    }

    public async Task ApplyCatalogVerificationAsync(IReadOnlyList<CatalogVerificationUpdate> updates, CancellationToken ct)
    {
        if (updates.Count == 0) return;
        foreach (var batch in updates.Chunk(250))
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var ids = batch.Select(x => x.FileId).ToArray();
            var files = await db.GameFiles.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var hashRows = await db.Hashes.Where(x => ids.Contains(x.GameFileId) && (x.Algorithm == "SHA1" || x.Algorithm == "CRC32")).ToListAsync(ct);
            var hashes = hashRows.GroupBy(x => (x.GameFileId, x.Algorithm)).ToDictionary(x => x.Key, x => x.First());
            var verifiedAt = DateTimeOffset.UtcNow;
            foreach (var update in batch)
            {
                if (!files.TryGetValue(update.FileId, out var file)) continue;
                file.CatalogStatus = update.Status;
                file.CatalogSource = update.Source;
                file.CatalogName = update.CatalogName;
                file.CatalogVerifiedAt = verifiedAt;
                SaveHash("SHA1", update.Sha1);
                SaveHash("CRC32", update.Crc32);

                void SaveHash(string algorithm, string? value)
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    if (hashes.TryGetValue((file.Id, algorithm), out var existing))
                    {
                        existing.Hash = value;
                        existing.CalculatedDate = verifiedAt;
                    }
                    else
                    {
                        var item = new FileHash { GameFileId = file.Id, Algorithm = algorithm, Hash = value, CalculatedDate = verifiedAt };
                        db.Hashes.Add(item);
                        hashes[(file.Id, algorithm)] = item;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RecalculatePreferredCopiesAsync(string? systemKey, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.GameFiles.Where(x => x.GameId != null);
        if (!string.IsNullOrWhiteSpace(systemKey)) query = query.Where(x => x.Game!.SystemDefinition!.Key == systemKey);
        var files = await query.ToListAsync(ct);
        foreach (var group in files.GroupBy(x => x.GameId))
            ApplyPreferredSelection(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetCopyPreferenceAsync(long fileId, bool manuallyPreferred, bool excluded, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var file = await db.GameFiles.SingleOrDefaultAsync(x => x.Id == fileId, ct);
        if (file is null) return;
        var gameId = file.GameId;
        if (manuallyPreferred && file.GameId.HasValue)
        {
            await db.GameFiles.Where(x => x.GameId == file.GameId && x.Id != file.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsManuallyPreferred, false).SetProperty(y => y.IsPreferred, false), ct);
        }
        file.IsManuallyPreferred = manuallyPreferred;
        file.IsExcluded = excluded;
        if (excluded) { file.IsManuallyPreferred = false; file.IsPreferred = false; }
        await db.SaveChangesAsync(ct);
        if (!gameId.HasValue) return;
        var copies = await db.GameFiles.Where(x => x.GameId == gameId).ToListAsync(ct);
        ApplyPreferredSelection(copies);
        await db.SaveChangesAsync(ct);
    }

    public async Task ExcludeNonPreferredCopiesAsync(IReadOnlyList<long> gameIds, CancellationToken ct)
    {
        if (gameIds.Count == 0) return;
        foreach (var batch in gameIds.Chunk(200))
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var files = await db.GameFiles.Where(x => x.GameId != null && batch.Contains(x.GameId!.Value) && x.Status != FileStatus.Missing).ToListAsync(ct);
            foreach (var group in files.GroupBy(x => x.GameId))
            {
                foreach (var file in group.Where(x => !x.IsPreferred && !x.IsExcluded)) file.IsExcluded = true;
                ApplyPreferredSelection(group);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ClearCopyOverridesAsync(IReadOnlyList<long> gameIds, CancellationToken ct)
    {
        if (gameIds.Count == 0) return;
        foreach (var batch in gameIds.Chunk(200))
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var files = await db.GameFiles.Where(x => x.GameId != null && batch.Contains(x.GameId!.Value)).ToListAsync(ct);
            foreach (var file in files) { file.IsManuallyPreferred = false; file.IsExcluded = false; }
            foreach (var group in files.GroupBy(x => x.GameId)) ApplyPreferredSelection(group);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetGamesWantedAsync(IReadOnlyList<long> gameIds, bool wanted, CancellationToken ct)
    {
        if (gameIds.Count == 0) return;
        foreach (var batch in gameIds.Chunk(200))
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.Games.Where(x => batch.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsWanted, wanted), ct);
        }
    }

    public async Task<IReadOnlyList<FuzzyMatchCandidate>> GetFuzzyMatchCandidatesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await db.Games.AsNoTracking().Select(x => new
        {
            x.Id,
            x.CanonicalTitle,
            x.NormalizedTitle,
            x.SystemDefinitionId,
            SystemName = x.SystemDefinition!.Name,
            FileCount = x.Files.Count(f => f.Status != FileStatus.Missing)
        }).ToListAsync(ct);
        var results = new List<FuzzyMatchCandidate>();
        foreach (var system in games.GroupBy(x => x.SystemDefinitionId))
        {
            var byId = system.ToDictionary(x => x.Id);
            var snapshots = system.Select(x => (x.Id, x.NormalizedTitle)).ToArray();
            foreach (var (aId, bId, similarity) in FuzzyTitleMatcher.FindSimilarPairs(snapshots))
            {
                var a = byId[aId]; var b = byId[bId];
                results.Add(new FuzzyMatchCandidate(a.Id, a.CanonicalTitle, b.Id, b.CanonicalTitle, a.SystemName, a.FileCount, b.FileCount, similarity));
            }
        }
        return results.OrderByDescending(x => x.Similarity).Take(300).ToList();
    }

    public async Task MergeGamesAsync(long keepGameId, long mergeGameId, CancellationToken ct)
    {
        if (keepGameId == mergeGameId) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GameFiles.Where(x => x.GameId == mergeGameId).ExecuteUpdateAsync(s => s.SetProperty(x => x.GameId, keepGameId), ct);
        await db.FileGroups.Where(x => x.GameId == mergeGameId).ExecuteUpdateAsync(s => s.SetProperty(x => x.GameId, keepGameId), ct);
        await db.Games.Where(x => x.Id == mergeGameId).ExecuteDeleteAsync(ct);
        var files = await db.GameFiles.Where(x => x.GameId == keepGameId).ToListAsync(ct);
        ApplyPreferredSelection(files);
        await db.SaveChangesAsync(ct);
        gamesByKey = null;
    }

    public async Task<IReadOnlyList<PreferredExportFile>> GetPreferredExportFilesAsync(bool onlyWanted, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.GameFiles.AsNoTracking().Where(x => x.IsPreferred && x.Status != FileStatus.Missing && x.Game != null);
        if (onlyWanted) query = query.Where(x => x.Game!.IsWanted);
        return await query
            .OrderBy(x => x.Game!.SystemDefinition!.Name).ThenBy(x => x.FileName)
            .Select(x => new PreferredExportFile(x.Game!.SystemDefinition!.Name, x.FullPath, x.FileName, x.IsDirectory))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TitleCleanupCandidate>> GetTitleCleanupCandidatesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await db.Games.AsNoTracking().Select(x => new
        {
            x.Id,
            x.CanonicalTitle,
            SystemName = x.SystemDefinition!.Name,
            VerifiedCatalogName = x.Files.Where(f => f.CatalogStatus == CatalogVerificationStatus.Verified && f.CatalogName != null)
                .OrderByDescending(f => f.IsPreferred).Select(f => f.CatalogName).FirstOrDefault()
        }).ToListAsync(ct);
        var rankPrefixedBySystem = games.GroupBy(x => x.SystemName)
            .ToDictionary(g => g.Key, g => TitlePrefixCleaner.HasWidespreadRankPrefixConvention(
                g.Count(x => TitlePrefixCleaner.TryStripAnyLeadingNumberPrefix(x.CanonicalTitle) is not null), g.Count()));
        var results = new List<TitleCleanupCandidate>();
        foreach (var game in games)
        {
            string suggested; string reason;
            if (!string.IsNullOrWhiteSpace(game.VerifiedCatalogName))
            {
                suggested = TitlePrefixCleaner.StripTrailingTags(game.VerifiedCatalogName).Trim();
                reason = "Matches verified catalog name";
            }
            else
            {
                var stripped = rankPrefixedBySystem[game.SystemName]
                    ? TitlePrefixCleaner.TryStripAnyLeadingNumberPrefix(game.CanonicalTitle)
                    : TitlePrefixCleaner.TryStripRankPrefix(game.CanonicalTitle);
                if (stripped is null) continue;
                suggested = TitlePrefixCleaner.StripTrailingTags(stripped).Trim();
                reason = "Removes numeric catalog-rank prefix";
            }
            if (suggested.Length == 0 || suggested == game.CanonicalTitle) continue;
            results.Add(new TitleCleanupCandidate(game.Id, game.SystemName, game.CanonicalTitle, suggested, reason));
        }
        return results.OrderBy(x => x.SystemName).ThenBy(x => x.CurrentTitle, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task ApplyTitleCleanupAsync(long gameId, string newTitle, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var game = await db.Games.SingleOrDefaultAsync(x => x.Id == gameId, ct);
        if (game is null) return;
        game.CanonicalTitle = newTitle;
        game.SortTitle = CreateSortTitle(newTitle);
        await db.SaveChangesAsync(ct);
        gamesByKey = null;
    }

    public async Task<IReadOnlyList<ExactTitleDuplicateGroup>> GetExactTitleDuplicateGroupsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await db.Games.AsNoTracking().Select(x => new
        {
            x.Id,
            x.CanonicalTitle,
            SystemName = x.SystemDefinition!.Name,
            FileCount = x.Files.Count(f => f.Status != FileStatus.Missing)
        }).ToListAsync(ct);
        return games.GroupBy(x => (x.SystemName, x.CanonicalTitle))
            .Where(g => g.Count() > 1)
            .Select(g => new ExactTitleDuplicateGroup(g.Key.SystemName, g.Key.CanonicalTitle, g.Sum(x => x.FileCount), g.Select(x => x.Id).ToList()))
            .OrderBy(x => x.SystemName).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The "keep" game is whichever copy already has the most files (ties broken by lowest Id, for
    // determinism) - an exact-title match means every candidate is equally "correct" as the surviving
    // Game row, so this is just a stable pick rather than a quality judgement; RecalculatePreferredCopiesAsync
    // (via ApplyPreferredSelection below) is what actually decides which physical file gets exported.
    public async Task MergeExactTitleDuplicateGroupAsync(IReadOnlyList<long> gameIds, CancellationToken ct)
    {
        if (gameIds.Count < 2) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await db.Games.Where(x => gameIds.Contains(x.Id))
            .Select(x => new { x.Id, FileCount = x.Files.Count(f => f.Status != FileStatus.Missing) })
            .ToListAsync(ct);
        var keepId = games.OrderByDescending(x => x.FileCount).ThenBy(x => x.Id).First().Id;
        foreach (var mergeId in gameIds.Where(id => id != keepId))
        {
            await db.GameFiles.Where(x => x.GameId == mergeId).ExecuteUpdateAsync(s => s.SetProperty(x => x.GameId, keepId), ct);
            await db.FileGroups.Where(x => x.GameId == mergeId).ExecuteUpdateAsync(s => s.SetProperty(x => x.GameId, keepId), ct);
            await db.Games.Where(x => x.Id == mergeId).ExecuteDeleteAsync(ct);
        }
        var files = await db.GameFiles.Where(x => x.GameId == keepId).ToListAsync(ct);
        ApplyPreferredSelection(files);
        await db.SaveChangesAsync(ct);
        gamesByKey = null;
    }

    public async Task<IReadOnlyList<DuplicateFileRow>> GetDuplicateFileReportAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // MarkExactDuplicatesAsync flags every file sharing a SHA-256 (not just the "extra" copies), so
        // grouping by hash here reconstructs each duplicate set and lets the caller compute reclaimable
        // space as (count - 1) * size per group - one copy of each set is still needed.
        var hashByFileId = await db.Hashes.Where(x => x.Algorithm == "SHA256").ToDictionaryAsync(x => x.GameFileId, x => x.Hash, ct);
        var files = await db.GameFiles.AsNoTracking().Where(x => x.Status == FileStatus.Duplicate && x.Game != null)
            .Select(x => new { x.Id, x.FullPath, x.FileName, x.Size, GameTitle = x.Game!.CanonicalTitle, SystemName = x.Game.SystemDefinition!.Name })
            .ToListAsync(ct);
        return files.Where(f => hashByFileId.ContainsKey(f.Id))
            .Select(f => new DuplicateFileRow(hashByFileId[f.Id], f.SystemName, f.GameTitle, f.FileName, f.FullPath, f.Size))
            .OrderBy(x => x.Sha256, StringComparer.Ordinal).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SystemDefinition>> GetSystemsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Systems.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct); }

    // One row per distinct Game, exactly matching the total shown at the bottom of the library view -
    // a game with 5 duplicate files is still 1 game, so this is naturally immune to file-level noise
    // (exact duplicates, multiple regions/revisions) without any extra filtering.
    public async Task<IReadOnlyDictionary<int, long>> GetGameCountsBySystemAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Games.AsNoTracking().GroupBy(x => x.SystemDefinitionId)
            .Select(g => new { SystemDefinitionId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.SystemDefinitionId, x => x.Count, ct);
    }

    private async Task EnsureGameCacheAsync(CancellationToken ct)
    {
        if (gamesByKey is not null) return;
        await gameCacheGate.WaitAsync(ct);
        try
        {
            if (gamesByKey is not null) return;
            await using var db = await factory.CreateDbContextAsync(ct);
            var databaseSystemToSource = sourceSystemDatabaseIds.ToDictionary(x => x.Value, x => x.Key);
            var games = await db.Games.AsNoTracking().ToListAsync(ct);
            gamesByKey = games.Where(x => databaseSystemToSource.ContainsKey(x.SystemDefinitionId))
                .ToDictionary(x => (databaseSystemToSource[x.SystemDefinitionId], x.NormalizedTitle));
        }
        finally { gameCacheGate.Release(); }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static int ScorePreference(GameFile file)
    {
        if (file.IsExcluded || file.Status == FileStatus.Missing) return -10000;
        var score = file.IsManuallyPreferred ? 5000 : file.CatalogStatus == CatalogVerificationStatus.Verified ? 1000 : 0;
        var name = file.CatalogName ?? file.FileName;
        if (ContainsAny(name, "(USA)", "(World)")) score += 120;
        else if (name.Contains("(Europe)", StringComparison.OrdinalIgnoreCase)) score += 80;
        else if (name.Contains("(Japan)", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (!string.IsNullOrWhiteSpace(file.Revision)) score += 10;
        if (ContainsAny(name, "(Beta", "(Proto", "(Demo", "(Sample", "[b", "(Pirate", "(Hack")) score -= 300;
        if (file.Status == FileStatus.Corrupt) score -= 1000;
        if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) score += 5;
        return score;
    }

    private static void ApplyPreferredSelection(IEnumerable<GameFile> gameFiles)
    {
        var files = gameFiles.ToArray();
        foreach (var file in files) { file.PreferenceScore = ScorePreference(file); file.IsPreferred = false; }
        var units = files.GroupBy(x => x.FileGroupId.HasValue ? $"G{x.FileGroupId.Value}" : $"F{x.Id}")
            .Where(x => x.All(f => !f.IsExcluded && f.Status != FileStatus.Missing))
            .Select(x => new { Files = x.ToArray(), Score = x.Max(f => f.PreferenceScore), Path = x.Select(f => f.FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First() })
            .OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (units is null) return;
        foreach (var file in units.Files) file.IsPreferred = true;
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static string CreateSortTitle(string title) => title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? $"{title[4..]}, The" : title;
}
