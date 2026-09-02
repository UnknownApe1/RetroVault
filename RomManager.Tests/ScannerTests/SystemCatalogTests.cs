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
}
