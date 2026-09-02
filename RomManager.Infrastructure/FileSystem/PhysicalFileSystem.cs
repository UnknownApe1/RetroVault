using System.Runtime.CompilerServices;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Infrastructure.FileSystem;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public async IAsyncEnumerable<FileCandidate> EnumerateFilesAsync(string root, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try { info = new FileInfo(path); if (!info.Exists) continue; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
                yield return new FileCandidate(info.FullName, info.Name, info.Extension.ToLowerInvariant(), info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
                await Task.Yield();
            }
            if (!recursive) continue;
            try { foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
    }

    public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }
}
