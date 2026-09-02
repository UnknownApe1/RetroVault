using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RomManager.Database.SQLite;

namespace RomManager.Database.Migrations;

internal static class SchemaMigrator
{
    public static async Task ApplyAsync(RomManagerDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await ExecuteAsync(db.Database.GetDbConnection(), "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=10000;", ct);
            await ExecuteAsync(db.Database.GetDbConnection(), "PRAGMA foreign_keys=ON; CREATE TABLE IF NOT EXISTS SchemaVersions (Version INTEGER NOT NULL PRIMARY KEY, AppliedUtc TEXT NOT NULL);", ct);
            if (await GetVersionAsync(db.Database.GetDbConnection(), ct) < 1)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV1}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (1, CURRENT_TIMESTAMP);COMMIT;", ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<long> GetVersionAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersions;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(ct); }

    private const string SqlV1 = """
CREATE TABLE Systems (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Key TEXT NOT NULL, Name TEXT NOT NULL, Manufacturer TEXT NOT NULL, Category TEXT NULL, Enabled INTEGER NOT NULL);
CREATE UNIQUE INDEX IX_Systems_Key ON Systems(Key);
CREATE TABLE ScanLocations (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Path TEXT NOT NULL, Enabled INTEGER NOT NULL, Recursive INTEGER NOT NULL, LastScanStarted TEXT NULL, LastScanCompleted TEXT NULL, FileCount INTEGER NOT NULL);
CREATE UNIQUE INDEX IX_ScanLocations_Path ON ScanLocations(Path);
CREATE TABLE SystemFormats (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SystemDefinitionId INTEGER NOT NULL, Extension TEXT NOT NULL, FormatName TEXT NOT NULL, FormatType TEXT NOT NULL, Priority INTEGER NOT NULL, RequiresHeaderCheck INTEGER NOT NULL, IsArchive INTEGER NOT NULL, IsMultiFile INTEGER NOT NULL, FOREIGN KEY(SystemDefinitionId) REFERENCES Systems(Id) ON DELETE CASCADE);
CREATE UNIQUE INDEX IX_SystemFormats_SystemDefinitionId_Extension_FormatName ON SystemFormats(SystemDefinitionId, Extension, FormatName);
CREATE TABLE Games (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, CanonicalTitle TEXT NOT NULL, SortTitle TEXT NOT NULL, NormalizedTitle TEXT NOT NULL, SystemDefinitionId INTEGER NOT NULL, Year INTEGER NULL, Status TEXT NOT NULL, Created TEXT NOT NULL, Updated TEXT NOT NULL, FOREIGN KEY(SystemDefinitionId) REFERENCES Systems(Id) ON DELETE CASCADE);
CREATE UNIQUE INDEX IX_Games_SystemDefinitionId_NormalizedTitle ON Games(SystemDefinitionId, NormalizedTitle);
CREATE TABLE FileGroups (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameId INTEGER NOT NULL, GroupType TEXT NOT NULL, DisplayName TEXT NOT NULL, DiscNumber INTEGER NULL, FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE);
CREATE TABLE GameFiles (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameId INTEGER NULL, ScanLocationId INTEGER NOT NULL, SystemFormatId INTEGER NULL, FileGroupId INTEGER NULL, FullPath TEXT NOT NULL, FileName TEXT NOT NULL, Extension TEXT NOT NULL, Size INTEGER NOT NULL, CreatedDate TEXT NOT NULL, ModifiedDate TEXT NOT NULL, QuickHash TEXT NULL, Region TEXT NULL, Language TEXT NULL, Revision TEXT NULL, Version TEXT NULL, DiscNumber INTEGER NULL, TrackNumber INTEGER NULL, Status TEXT NOT NULL, LastSeen TEXT NOT NULL, FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE SET NULL, FOREIGN KEY(ScanLocationId) REFERENCES ScanLocations(Id) ON DELETE CASCADE, FOREIGN KEY(SystemFormatId) REFERENCES SystemFormats(Id), FOREIGN KEY(FileGroupId) REFERENCES FileGroups(Id) ON DELETE SET NULL);
CREATE UNIQUE INDEX IX_GameFiles_FullPath ON GameFiles(FullPath);
CREATE INDEX IX_GameFiles_QuickHash_Size ON GameFiles(QuickHash, Size);
CREATE TABLE Hashes (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileId INTEGER NOT NULL, Algorithm TEXT NOT NULL, Hash TEXT NOT NULL, CalculatedDate TEXT NOT NULL, FOREIGN KEY(GameFileId) REFERENCES GameFiles(Id) ON DELETE CASCADE);
""";
}
