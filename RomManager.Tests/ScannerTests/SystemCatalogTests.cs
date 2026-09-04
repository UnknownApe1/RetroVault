using RomManager.Core.Models;
using RomManager.Formats.FormatDefinitions;

namespace RomManager.Tests.ScannerTests;

public sealed class SystemCatalogTests
{
    [Fact]
    public void UsesFolderHintToResolveAmbiguousIso()
    {
        var catalog = new SystemCatalog();
        var file = new FileCandidate(Path.Combine("D:", "ROMs", "PS2", "Game.iso"), "Game.iso", ".iso", 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var result = catalog.Identify(file);
        Assert.Equal("PS2", result.System?.Key); Assert.True(result.Confidence > .5);
    }

    [Theory]
    [InlineData("SATURN")]
    [InlineData("SEGACD")]
    [InlineData("3DO")]
    [InlineData("DREAMCAST")]
    [InlineData("JAGCD")]
    [InlineData("PCECD")]
    [InlineData("NEOGEOCD")]
    [InlineData("CDI")]
    [InlineData("CD32")]
    [InlineData("FMTOWNS")]
    public void RecognizesCueBinTrackFilesAsCandidatesForEveryCueSystem(string systemKey)
    {
        // Regression: these systems' .cue entries used to have no matching .bin format at all, so the CD
        // data track a .cue references was invisible to the scanner (and therefore to Export Good Roms).
        var catalog = new SystemCatalog();
        Assert.True(catalog.IsCandidate(".bin"));
        var file = new FileCandidate(Path.Combine("D:", "ROMs", systemKey, "Game (Track 1).bin"), "Game (Track 1).bin", ".bin", 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var result = catalog.Identify(file);
        Assert.Equal(systemKey, result.System?.Key);
        Assert.Equal(FileCategory.DiscImage, result.Format?.FormatType);
    }
}
