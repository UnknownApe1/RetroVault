namespace RomManager.Core.Models;

public enum FileCategory { Rom, CartridgeImage, DiscImage, MultiFileDisc, Archive, ArcadeSet, FloppyImage, TapeImage, Package, DirectoryGame, Unknown }
public enum FileStatus { Normal, Duplicate, Missing, Changed, Unsupported, Corrupt, Unverified, Grouped }
public enum GameStatus { Active, Missing, NeedsReview }
public enum CatalogVerificationStatus { Unknown, Verified, NoMatch, Unsupported, Error }

public sealed class SystemDefinition
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Manufacturer { get; set; }
    public string? Category { get; set; }
    public bool Enabled { get; set; } = true;
    public List<SystemFormat> Formats { get; set; } = [];
}

public sealed class SystemFormat
{
    public int Id { get; set; }
    public int SystemDefinitionId { get; set; }
    public SystemDefinition? SystemDefinition { get; set; }
    public required string Extension { get; set; }
    public required string FormatName { get; set; }
    public FileCategory FormatType { get; set; }
    public int Priority { get; set; }
    public bool RequiresHeaderCheck { get; set; }
    public bool IsArchive { get; set; }
    public bool IsMultiFile { get; set; }
}

public sealed class ScanLocation
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public DateTimeOffset? LastScanStarted { get; set; }
    public DateTimeOffset? LastScanCompleted { get; set; }
    public long FileCount { get; set; }
    public List<GameFile> Files { get; set; } = [];
}

public sealed class Game
{
    public long Id { get; set; }
    public required string CanonicalTitle { get; set; }
    public required string SortTitle { get; set; }
    public required string NormalizedTitle { get; set; }
    public int SystemDefinitionId { get; set; }
    public SystemDefinition? SystemDefinition { get; set; }
    public int? Year { get; set; }
    public GameStatus Status { get; set; } = GameStatus.Active;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public List<GameFile> Files { get; set; } = [];
    public List<FileGroup> FileGroups { get; set; } = [];
}

public sealed class GameFile
{
    public long Id { get; set; }
    public long? GameId { get; set; }
    public Game? Game { get; set; }
    public int ScanLocationId { get; set; }
    public ScanLocation? ScanLocation { get; set; }
    public int? SystemFormatId { get; set; }
    public SystemFormat? SystemFormat { get; set; }
    public long? FileGroupId { get; set; }
    public FileGroup? FileGroup { get; set; }
    public required string FullPath { get; set; }
    public required string FileName { get; set; }
    public required string Extension { get; set; }
    public long Size { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset ModifiedDate { get; set; }
    public string? QuickHash { get; set; }
    public string? Region { get; set; }
    public string? Language { get; set; }
    public string? Revision { get; set; }
    public string? Version { get; set; }
    public int? DiscNumber { get; set; }
    public int? TrackNumber { get; set; }
    public FileStatus Status { get; set; } = FileStatus.Unverified;
    public CatalogVerificationStatus CatalogStatus { get; set; } = CatalogVerificationStatus.Unknown;
    public string? CatalogSource { get; set; }
    public string? CatalogName { get; set; }
    public DateTimeOffset? CatalogVerifiedAt { get; set; }
    public bool IsPreferred { get; set; }
    public bool IsManuallyPreferred { get; set; }
    public bool IsExcluded { get; set; }
    public int PreferenceScore { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public List<FileHash> Hashes { get; set; } = [];
}

public sealed class FileGroup
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public Game? Game { get; set; }
    public required string GroupType { get; set; }
    public required string DisplayName { get; set; }
    public int? DiscNumber { get; set; }
    public List<GameFile> Files { get; set; } = [];
}

public sealed class FileHash
{
    public long Id { get; set; }
    public long GameFileId { get; set; }
    public GameFile? GameFile { get; set; }
    public required string Algorithm { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset CalculatedDate { get; set; } = DateTimeOffset.UtcNow;
}
