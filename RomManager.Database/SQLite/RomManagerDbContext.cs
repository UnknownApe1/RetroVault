using Microsoft.EntityFrameworkCore;
using RomManager.Core.Models;

namespace RomManager.Database.SQLite;

public sealed class RomManagerDbContext(DbContextOptions<RomManagerDbContext> options) : DbContext(options)
{
    public DbSet<SystemDefinition> Systems => Set<SystemDefinition>();
    public DbSet<SystemFormat> SystemFormats => Set<SystemFormat>();
    public DbSet<ScanLocation> ScanLocations => Set<ScanLocation>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameFile> GameFiles => Set<GameFile>();
    public DbSet<FileGroup> FileGroups => Set<FileGroup>();
    public DbSet<FileHash> Hashes => Set<FileHash>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SystemDefinition>().HasIndex(x => x.Key).IsUnique();
        b.Entity<SystemFormat>().HasIndex(x => new { x.SystemDefinitionId, x.Extension, x.FormatName }).IsUnique();
        b.Entity<ScanLocation>().HasIndex(x => x.Path).IsUnique();
        b.Entity<Game>().HasIndex(x => new { x.SystemDefinitionId, x.NormalizedTitle }).IsUnique();
        b.Entity<Game>().HasIndex(x => x.SortTitle);
        b.Entity<Game>().HasIndex(x => new { x.SystemDefinitionId, x.SortTitle });
        b.Entity<GameFile>().HasIndex(x => x.FullPath).IsUnique();
        b.Entity<GameFile>().HasIndex(x => new { x.QuickHash, x.Size });
        b.Entity<GameFile>().HasIndex(x => new { x.GameId, x.Status });
        b.Entity<GameFile>().HasIndex(x => x.Status);
        b.Entity<GameFile>().HasIndex(x => new { x.ScanLocationId, x.LastSeen });
        b.Entity<GameFile>().HasIndex(x => x.CatalogStatus);
        b.Entity<GameFile>().HasIndex(x => new { x.GameId, x.IsPreferred });
        b.Entity<FileGroup>().HasIndex(x => new { x.GameId, x.DiscNumber, x.DisplayName });
        b.Entity<FileHash>().HasIndex(x => new { x.GameFileId, x.Algorithm });
        b.Entity<FileHash>().HasIndex(x => new { x.Algorithm, x.Hash });
        b.Entity<GameFile>().Property(x => x.Status).HasConversion<string>();
        b.Entity<GameFile>().Property(x => x.CatalogStatus).HasConversion<string>();
        b.Entity<Game>().Property(x => x.Status).HasConversion<string>();
        b.Entity<SystemFormat>().Property(x => x.FormatType).HasConversion<string>();
        b.Entity<GameFile>().HasOne(x => x.Game).WithMany(x => x.Files).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<GameFile>().HasOne(x => x.ScanLocation).WithMany(x => x.Files).HasForeignKey(x => x.ScanLocationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GameFile>().HasOne(x => x.FileGroup).WithMany(x => x.Files).HasForeignKey(x => x.FileGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}
