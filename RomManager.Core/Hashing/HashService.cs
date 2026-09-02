using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using RomManager.Core.Services;

namespace RomManager.Core.Hashing;

public sealed class HashService(IFileSystem fileSystem) : IHashService
{
    private const int SampleSize = 64 * 1024;

    public async Task<string> ComputeQuickHashAsync(string path, long size, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(path, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var buffer = ArrayPool<byte>.Shared.Rent(SampleSize);
        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);
            hash.AppendData(buffer, 0, read);
            if (stream.CanSeek && size > SampleSize)
            {
                stream.Seek(Math.Max(0, size - SampleSize), SeekOrigin.Begin);
                read = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(path, cancellationToken);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
