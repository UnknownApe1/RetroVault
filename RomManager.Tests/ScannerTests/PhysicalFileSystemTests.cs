using RomManager.Infrastructure.FileSystem;

namespace RomManager.Tests.ScannerTests;

public sealed class PhysicalFileSystemTests
{
    [Fact]
    public async Task EnumeratesNestedFilesWhenRecursive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rom-manager-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path.Combine(root, "SNES"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "root.nes"), "a"); await File.WriteAllTextAsync(Path.Combine(root, "SNES", "nested.sfc"), "b");
            var found = new List<string>(); await foreach (var file in new PhysicalFileSystem().EnumerateFilesAsync(root, true, CancellationToken.None)) found.Add(file.FileName);
            Assert.Equal(2, found.Count); Assert.Contains("nested.sfc", found);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task YieldsOneDirectoryCandidateForAPs3StyleGameFolderInsteadOfRecursingIntoIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rom-manager-{Guid.NewGuid():N}");
        var gameFolder = Path.Combine(root, "BLUS30057-[Army of Two TM]");
        Directory.CreateDirectory(Path.Combine(gameFolder, "USRDIR"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(gameFolder, "PARAM.SFO"), [0, 1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(gameFolder, "USRDIR", "EBOOT.BIN"), "binary data");
            await File.WriteAllTextAsync(Path.Combine(root, "loose.iso"), "iso bytes");

            var found = new List<Core.Models.FileCandidate>();
            await foreach (var file in new PhysicalFileSystem().EnumerateFilesAsync(root, true, CancellationToken.None)) found.Add(file);

            var loose = Assert.Single(found, x => x.FileName == "loose.iso");
            Assert.False(loose.IsDirectoryGame);
            var directoryCandidate = Assert.Single(found, x => x.IsDirectoryGame);
            Assert.Equal("BLUS30057-[Army of Two TM]", directoryCandidate.FileName);
            Assert.Equal(".ps3dir", directoryCandidate.Extension);
            Assert.True(directoryCandidate.Size > 0);
            Assert.EndsWith("PARAM.SFO", directoryCandidate.IdentityFilePath);
            Assert.DoesNotContain(found, x => x.FileName is "EBOOT.BIN" or "PARAM.SFO");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DoesNotTreatAnOrdinaryFolderAsADirectoryGame()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rom-manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "SNES"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "SNES", "game.sfc"), "rom bytes");
            var found = new List<Core.Models.FileCandidate>();
            await foreach (var file in new PhysicalFileSystem().EnumerateFilesAsync(root, true, CancellationToken.None)) found.Add(file);
            Assert.DoesNotContain(found, x => x.IsDirectoryGame);
            Assert.Contains(found, x => x.FileName == "game.sfc");
        }
        finally { Directory.Delete(root, true); }
    }
}
