using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Infrastructure.FileSystem;

public sealed class PhysicalFileSystem(ILogger<PhysicalFileSystem>? logger = null) : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public async IAsyncEnumerable<FileCandidate> EnumerateFilesAsync(string root, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var enumerationErrors = 0;
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { enumerationErrors++; logger?.LogWarning(ex, "Could not enumerate files in {Directory}", directory); continue; }
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try { info = new FileInfo(path); if (!info.Exists) { logger?.LogDebug("Skipped vanished file {Path}", path); continue; } }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { enumerationErrors++; logger?.LogWarning(ex, "Could not read file information for {Path}", path); continue; }
                yield return new FileCandidate(info.FullName, info.Name, info.Extension.ToLowerInvariant(), info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
                await Task.Yield();
            }
            if (!recursive) continue;
            try { foreach (var child in Directory.GetDirectories(directory)) pending.Push(child); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { enumerationErrors++; logger?.LogWarning(ex, "Could not enumerate subdirectories in {Directory}", directory); }
        }
        if (enumerationErrors > 0) throw new IncompleteFileEnumerationException(root, enumerationErrors);
    }

    public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }
}
