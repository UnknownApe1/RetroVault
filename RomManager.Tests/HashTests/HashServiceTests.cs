using RomManager.Core.Hashing;
using RomManager.Infrastructure.FileSystem;

namespace RomManager.Tests.HashTests;

public sealed class HashServiceTests
{
    [Fact]
    public async Task QuickHashIsStableAndContentSensitive()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "rom data one"); var service = new HashService(new PhysicalFileSystem());
            var one = await service.ComputeQuickHashAsync(path, new FileInfo(path).Length, CancellationToken.None);
            var again = await service.ComputeQuickHashAsync(path, new FileInfo(path).Length, CancellationToken.None);
            await File.WriteAllTextAsync(path, "rom data two");
            var two = await service.ComputeQuickHashAsync(path, new FileInfo(path).Length, CancellationToken.None);
            Assert.Equal(one, again); Assert.NotEqual(one, two);
        }
        finally { File.Delete(path); }
    }
}
