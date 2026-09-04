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
            string[] subdirectories;
            try { subdirectories = Directory.GetDirectories(directory); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { enumerationErrors++; logger?.LogWarning(ex, "Could not enumerate subdirectories in {Directory}", directory); continue; }
            foreach (var child in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileCandidate? directoryGame;
                try { directoryGame = TryBuildDirectoryGameCandidate(child); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { enumerationErrors++; logger?.LogWarning(ex, "Could not inspect folder {Path}", child); continue; }
                if (directoryGame is not null) { yield return directoryGame; await Task.Yield(); }
                else pending.Push(child);
            }
        }
        if (enumerationErrors > 0) throw new IncompleteFileEnumerationException(root, enumerationErrors);
    }

    // A folder that carries PARAM.SFO at its root or under PS3_GAME/ is a decrypted RPCS3-style PS3
    // install - one game as a directory tree, not a single file. It's identified via the sentinel
    // extension ".ps3dir" (only PS3 in the format catalog claims it, so resolution is unambiguous) and
    // treated as one candidate whose "file" is the whole directory; its contents are never recursed into
    // as separate loose-file candidates. Other systems' folder-based installs are not yet detected.
    private const string DirectoryGameExtension = ".ps3dir";

    private static FileCandidate? TryBuildDirectoryGameCandidate(string directory)
    {
        var direct = Path.Combine(directory, "PARAM.SFO");
        var nested = Path.Combine(directory, "PS3_GAME", "PARAM.SFO");
        var paramSfoPath = File.Exists(direct) ? direct : File.Exists(nested) ? nested : null;
        if (paramSfoPath is null) return null;

        var info = new DirectoryInfo(directory);
        long totalSize = 0;
        var modified = info.LastWriteTimeUtc;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var file = new FileInfo(path);
            totalSize += file.Length;
            if (file.LastWriteTimeUtc > modified) modified = file.LastWriteTimeUtc;
        }
        return new FileCandidate(info.FullName, info.Name, DirectoryGameExtension, totalSize, info.CreationTimeUtc, modified, IsDirectoryGame: true, IdentityFilePath: paramSfoPath);
    }

    public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }
}
