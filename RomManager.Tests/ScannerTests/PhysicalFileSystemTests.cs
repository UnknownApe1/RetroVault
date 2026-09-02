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
}
