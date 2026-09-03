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

    public async Task<LibraryExportOperationResult> ExportPreferredCopiesAsync(string destinationRoot, IProgress<LibraryExportProgress>? progress, CancellationToken ct)
    {
        var files = await repository.GetPreferredExportFilesAsync(ct);
        long copied = 0, skipped = 0, errors = 0, completed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var systemFolder = Path.Combine(destinationRoot, SanitizeFolderName(file.SystemName));
                Directory.CreateDirectory(systemFolder);
                var destinationPath = Path.Combine(systemFolder, file.FileName);
                if (IsAlreadyExported(file.FullPath, destinationPath)) skipped++;
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

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in InvalidFolderNameCharacters) name = name.Replace(c, '_');
        return name;
    }
}
