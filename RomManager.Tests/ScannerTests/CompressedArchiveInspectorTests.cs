using System.IO.Compression;
using RomManager.Core.Models;
using RomManager.Formats.FormatDefinitions;
using RomManager.Formats.Parsers;
using RomManager.Infrastructure.FileSystem;

namespace RomManager.Tests.ScannerTests;

public sealed class CompressedArchiveInspectorTests
{
    [Fact]
    public async Task IdentifiesZipContainingARecognizedRom()
    {
        var path = CreateTempPath(".zip");
        try
        {
            using (var stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using var entry = zip.CreateEntry("Game.nes").Open();
                entry.Write("fake nes data"u8);
            }

            var result = await IdentifyAsync(path, ".zip");

            Assert.NotNull(result?.System);
            Assert.Equal("NES", result!.System!.Key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IdentifiesSevenZipContainingARecognizedRom()
    {
        // Fixtures/sample.7z was pre-built with 7-Zip (SharpCompress can read 7z but not write it)
        // and contains a single entry "Game.sfc".
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.7z");

        var result = await IdentifyAsync(fixturePath, ".7z");

        Assert.NotNull(result?.System);
        Assert.Equal("SNES", result!.System!.Key);
    }

    [Fact]
    public async Task ReturnsNullForACorruptArchiveInsteadOfThrowing()
    {
        var path = CreateTempPath(".rar");
        try
        {
            await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03]);

            var result = await IdentifyAsync(path, ".rar");

            Assert.Null(result);
        }
        finally { File.Delete(path); }
    }

    private static async Task<IdentificationResult?> IdentifyAsync(string path, string extension)
    {
        var catalog = new SystemCatalog();
        var inspector = new CompressedArchiveInspector(new PhysicalFileSystem(), catalog);
        var info = new FileInfo(path);
        var candidate = new FileCandidate(info.FullName, info.Name, extension, info.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return await inspector.IdentifyContentsAsync(candidate, CancellationToken.None);
    }

    private static string CreateTempPath(string extension) => Path.Combine(Path.GetTempPath(), $"rom-manager-archive-{Guid.NewGuid():N}{extension}");
}
