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

    [Fact]
    public void ResolvesSystemFromAFolderNameThatEmbedsTheKeyAsAWholeToken()
    {
        // Regression: a real user's "PS3_Games" folder (key never appears as an isolated "/PS3/" segment)
        // was resolving PS3 .iso files to PS2 instead, because PS2's higher raw priority for .iso won once
        // no folder hint applied. The key must still match as a whole token, not as an arbitrary substring.
        var catalog = new SystemCatalog();
        var file = new FileCandidate(Path.Combine("D:", "Games", "Roms", "PS3_Games", "Some Game (USA).iso"), "Some Game (USA).iso", ".iso", 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var result = catalog.Identify(file);
        Assert.Equal("PS3", result.System?.Key);
    }

    [Fact]
    public void DoesNotMatchAKeyThatIsOnlyASubstringOfAnUnrelatedWord()
    {
        var catalog = new SystemCatalog();
        var file = new FileCandidate(Path.Combine("D:", "Games", "Roms", "MAGIC_Collection", "Some Game (USA).iso"), "Some Game (USA).iso", ".iso", 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var result = catalog.Identify(file);
        Assert.NotEqual("GC", result.System?.Key);
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
