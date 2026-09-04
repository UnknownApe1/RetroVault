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
            var version = await GetVersionAsync(db.Database.GetDbConnection(), ct);
            if (version < 1)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV1}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (1, CURRENT_TIMESTAMP);COMMIT;", ct);
                version = 1;
            }
            if (version < 2)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV2}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (2, CURRENT_TIMESTAMP);COMMIT;", ct);
                version = 2;
            }
            if (version < 3)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV3}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (3, CURRENT_TIMESTAMP);COMMIT;", ct);
                version = 3;
            }
            if (version < 4)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV4}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (4, CURRENT_TIMESTAMP);COMMIT;", ct);
                version = 4;
            }
            if (version < 5)
            {
                await ExecuteAsync(db.Database.GetDbConnection(), $"BEGIN IMMEDIATE;{SqlV5}INSERT INTO SchemaVersions(Version, AppliedUtc) VALUES (5, CURRENT_TIMESTAMP);COMMIT;", ct);
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

    private const string SqlV2 = """
CREATE INDEX IF NOT EXISTS IX_Games_SortTitle ON Games(SortTitle);
CREATE INDEX IF NOT EXISTS IX_Games_SystemDefinitionId_SortTitle ON Games(SystemDefinitionId, SortTitle);
CREATE INDEX IF NOT EXISTS IX_GameFiles_GameId_Status ON GameFiles(GameId, Status);
CREATE INDEX IF NOT EXISTS IX_GameFiles_Status ON GameFiles(Status);
CREATE INDEX IF NOT EXISTS IX_GameFiles_ScanLocationId_LastSeen ON GameFiles(ScanLocationId, LastSeen);
CREATE INDEX IF NOT EXISTS IX_FileGroups_GameId_DiscNumber_DisplayName ON FileGroups(GameId, DiscNumber, DisplayName);
CREATE INDEX IF NOT EXISTS IX_Hashes_GameFileId_Algorithm ON Hashes(GameFileId, Algorithm);
CREATE INDEX IF NOT EXISTS IX_Hashes_Algorithm_Hash ON Hashes(Algorithm, Hash);
""";

    private const string SqlV3 = """
ALTER TABLE GameFiles ADD COLUMN CatalogStatus TEXT NOT NULL DEFAULT 'Unknown';
ALTER TABLE GameFiles ADD COLUMN CatalogSource TEXT NULL;
ALTER TABLE GameFiles ADD COLUMN CatalogName TEXT NULL;
ALTER TABLE GameFiles ADD COLUMN CatalogVerifiedAt TEXT NULL;
ALTER TABLE GameFiles ADD COLUMN IsPreferred INTEGER NOT NULL DEFAULT 0;
ALTER TABLE GameFiles ADD COLUMN IsManuallyPreferred INTEGER NOT NULL DEFAULT 0;
ALTER TABLE GameFiles ADD COLUMN IsExcluded INTEGER NOT NULL DEFAULT 0;
ALTER TABLE GameFiles ADD COLUMN PreferenceScore INTEGER NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS IX_GameFiles_CatalogStatus ON GameFiles(CatalogStatus);
CREATE INDEX IF NOT EXISTS IX_GameFiles_GameId_IsPreferred ON GameFiles(GameId, IsPreferred);
""";

    private const string SqlV4 = """
ALTER TABLE Games ADD COLUMN IsWanted INTEGER NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS IX_Games_IsWanted ON Games(IsWanted);
""";

    private const string SqlV5 = """
ALTER TABLE GameFiles ADD COLUMN IsDirectory INTEGER NOT NULL DEFAULT 0;
""";
}
