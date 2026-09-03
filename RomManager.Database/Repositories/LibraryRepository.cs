using Microsoft.EntityFrameworkCore;
using RomManager.Core.Models;
using RomManager.Core.Services;
using RomManager.Database.SQLite;
using RomManager.Database.Migrations;

namespace RomManager.Database.Repositories;

public sealed class LibraryRepository(IDbContextFactory<RomManagerDbContext> factory, ISystemDefinitionProvider definitions) : ILibraryRepository
{
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
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.ScanLocations.FindAsync([id], ct); if (item is not null) { db.Remove(item); await db.SaveChangesAsync(ct); await db.Games.Where(x => !x.Files.Any()).ExecuteDeleteAsync(ct); } }

    public async Task<GameFile?> FindFileByPathAsync(string path, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.GameFiles.SingleOrDefaultAsync(x => x.FullPath == path, ct); }

    public async Task UpsertFileAsync(GameFile file, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (file.Id == 0) db.GameFiles.Add(file); else db.GameFiles.Update(file);
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
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = (await definitions.LoadAsync(ct)).Single(x => x.Id == sourceSystemId);
        var systemId = await db.Systems.Where(x => x.Key == source.Key).Select(x => x.Id).SingleAsync(ct);
        var game = await db.Games.SingleOrDefaultAsync(x => x.SystemDefinitionId == systemId && x.NormalizedTitle == normalizedTitle, ct);
        if (game is not null) return game;
        game = new Game { CanonicalTitle = title, SortTitle = CreateSortTitle(title), NormalizedTitle = normalizedTitle, SystemDefinitionId = systemId };
        db.Games.Add(game); await db.SaveChangesAsync(ct); return game;
    }

    public async Task<int> ResolveFormatIdAsync(string systemKey, string extension, string formatName, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SystemFormats.Where(x => x.SystemDefinition!.Key == systemKey && x.Extension == extension && x.FormatName == formatName).Select(x => x.Id).SingleAsync(ct);
    }

    public async Task<long> GetOrCreateFileGroupAsync(long gameId, string displayName, int? discNumber, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var group = await db.FileGroups.SingleOrDefaultAsync(x => x.GameId == gameId && x.DiscNumber == discNumber && x.DisplayName == displayName, ct);
        if (group is not null) return group.Id;
        group = new FileGroup { GameId = gameId, GroupType = "MULTI_FILE_DISC", DisplayName = displayName, DiscNumber = discNumber };
        db.FileGroups.Add(group); await db.SaveChangesAsync(ct); return group.Id;
    }

    public async Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string? search, int? systemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Games.AsNoTracking().AsQueryable();
        if (systemId.HasValue) q = q.Where(x => x.SystemDefinitionId == systemId);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); q = q.Where(x => EF.Functions.Like(x.CanonicalTitle, $"%{term}%") || x.Files.Any(f => EF.Functions.Like(f.FileName, $"%{term}%") || EF.Functions.Like(f.FullPath, $"%{term}%"))); }
        var rows = await q.OrderBy(x => x.SortTitle).Take(5000).Select(x => new
        {
            x.Id,
            Title = x.CanonicalTitle,
            System = x.SystemDefinition!.Name,
            FileCount = x.Files.Count,
            DuplicateCount = x.Files.Count(f => f.Status == FileStatus.Duplicate),
            MissingCount = x.Files.Count(f => f.Status == FileStatus.Missing)
        }).ToListAsync(ct);
        return rows.Select(x => new GameSummary(x.Id, x.Title, x.System, x.FileCount, x.DuplicateCount,
            x.MissingCount > 0 ? FileStatus.Missing : x.DuplicateCount > 0 ? FileStatus.Duplicate : FileStatus.Normal)).ToList();
    }

    public async Task<Game?> GetGameDetailsAsync(long id, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Games.AsNoTracking().Include(x => x.SystemDefinition).Include(x => x.Files).ThenInclude(x => x.SystemFormat).SingleOrDefaultAsync(x => x.Id == id, ct); }

    public async Task<LibraryCounts> GetCountsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return new(await db.Games.LongCountAsync(ct), await db.GameFiles.LongCountAsync(ct), await db.GameFiles.LongCountAsync(x => x.Status == FileStatus.Duplicate, ct), await db.GameFiles.LongCountAsync(x => x.Status == FileStatus.Missing, ct)); }

    public async Task<IReadOnlyList<SystemDefinition>> GetSystemsAsync(CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Systems.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct); }

    private static string CreateSortTitle(string title) => title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? $"{title[4..]}, The" : title;
}
