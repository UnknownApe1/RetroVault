using Microsoft.Extensions.Logging;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Infrastructure.Export;

// Copies each game's preferred, non-excluded copy into a destination folder organized by system.
// Read-only against the source library: files are only ever opened for reading here, matching the
// application's core guarantee that indexed ROMs are never moved, renamed, or modified in place.
public sealed class LibraryExportService(ILibraryRepository repository, ILogger<LibraryExportService> logger) : ILibraryExportService
{
    private static readonly char[] InvalidFolderNameCharacters = System.IO.Path.GetInvalidFileNameChars();

    public async Task<LibraryExportOperationResult> ExportPreferredCopiesAsync(string destinationRoot, bool onlyWanted, IProgress<LibraryExportProgress>? progress, CancellationToken ct)
    {
        var files = await repository.GetPreferredExportFilesAsync(onlyWanted, ct);
        long copied = 0, skipped = 0, errors = 0, completed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var systemFolder = Path.Combine(destinationRoot, SanitizeFolderName(file.SystemName));
                Directory.CreateDirectory(systemFolder);
                var destinationPath = Path.Combine(systemFolder, file.FileName);
                if (file.IsDirectory)
                {
                    if (IsAlreadyExportedDirectory(file.FullPath, destinationPath)) skipped++;
                    else { CopyDirectory(file.FullPath, destinationPath); copied++; }
                }
                else if (IsAlreadyExported(file.FullPath, destinationPath)) skipped++;
                else { File.Copy(file.FullPath, destinationPath, overwrite: true); copied++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not export {Path}", file.FullPath);
                errors++;
            }
            completed++;
            progress?.Report(new LibraryExportProgress(completed, files.Count, file.FileName));
        }
        return new LibraryExportOperationResult(copied, skipped, errors);
    }

    private static bool IsAlreadyExported(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return false;
        var source = new FileInfo(sourcePath);
        var destination = new FileInfo(destinationPath);
        return source.Exists && source.Length == destination.Length;
    }

    // A directory game can be many GB, so "already exported" is judged by total size rather than a
    // per-file walk-and-compare; this mirrors the single-file size check above and is enough to make
    // repeated export runs skip work without re-copying an entire unchanged install every time.
    private static bool IsAlreadyExportedDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(destinationPath)) return false;
        return DirectorySize(sourcePath) == DirectorySize(destinationPath);
    }

    private static long DirectorySize(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : -1;

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destinationPath = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in InvalidFolderNameCharacters) name = name.Replace(c, '_');
        return name;
    }
}
