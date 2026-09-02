using RomManager.Core.Models;
using RomManager.Core.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RomManager.Core.Scanner;

public sealed class LibraryScanner(IFileSystem fileSystem, IFormatIdentifier formats, IArchiveInspector archives, IFileNameParser parser, IHashService hashes, ILibraryRepository repository, ILogger<LibraryScanner> logger) : ILibraryScanner
{
    public async Task<ScanResult> ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var totals = new Counters();
        foreach (var location in await repository.GetScanLocationsAsync(true, cancellationToken))
            totals.Add(await ScanLocationAsync(location, progress, cancellationToken));
        return totals.Result();
    }

    public async Task<ScanResult> ScanLocationAsync(ScanLocation location, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!fileSystem.DirectoryExists(location.Path)) { logger.LogWarning("Scan location unavailable: {Location}", location.Path); return new ScanResult(0, 0, 0, 0, 0, 0, [$"Location unavailable: {location.Path}"]); }
        var started = DateTimeOffset.UtcNow;
        await repository.BeginScanAsync(location.Id, started, cancellationToken);
        logger.LogInformation("Scan started for {Location}", location.Path);
        var counts = new Counters();
        var progressClock = Stopwatch.StartNew();
        await foreach (var candidate in fileSystem.EnumerateFilesAsync(location.Path, location.Recursive, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Discovered++;
            if (!formats.IsCandidate(candidate.Extension)) { counts.Skipped++; Report(candidate.FullPath); continue; }
            try
            {
                var existing = await repository.FindFileByPathAsync(candidate.FullPath, cancellationToken);
                if (existing is not null && existing.Size == candidate.Size && existing.ModifiedDate == candidate.Modified)
                {
                    existing.LastSeen = started;
                    if (existing.Status == FileStatus.Missing) existing.Status = FileStatus.Normal;
                    await repository.UpsertFileAsync(existing, cancellationToken);
                    counts.Skipped++; Report(candidate.FullPath); continue;
                }

                var identified = candidate.Extension == ".zip" ? await archives.IdentifyContentsAsync(candidate, cancellationToken) ?? formats.Identify(candidate) : formats.Identify(candidate);
                if (identified.System is null || identified.Format is null) { counts.Skipped++; Report(candidate.FullPath); continue; }
                var parsed = parser.Parse(candidate.FileName);
                var game = await repository.GetOrCreateGameAsync(parsed.Title, parsed.NormalizedTitle, identified.System.Id, cancellationToken);
                var file = existing ?? new GameFile { FullPath = candidate.FullPath, FileName = candidate.FileName, Extension = candidate.Extension, ScanLocationId = location.Id };
                file.GameId = game.Id;
                file.SystemFormatId = await repository.ResolveFormatIdAsync(identified.System.Key, identified.Format.Extension, identified.Format.FormatName, cancellationToken);
                file.FileName = candidate.FileName;
                file.Extension = candidate.Extension;
                file.Size = candidate.Size;
                file.CreatedDate = candidate.Created;
                file.ModifiedDate = candidate.Modified;
                file.Region = string.Join(", ", parsed.Regions);
                file.Language = string.Join(", ", parsed.Languages);
                file.Revision = parsed.Revision;
                file.Version = parsed.Version;
                file.DiscNumber = parsed.DiscNumber;
                file.TrackNumber = parsed.TrackNumber;
                if (identified.Format.IsMultiFile || parsed.TrackNumber.HasValue || (candidate.Extension == ".bin" && identified.Format.FormatType == FileCategory.DiscImage))
                    file.FileGroupId = await repository.GetOrCreateFileGroupAsync(game.Id, parsed.DiscNumber.HasValue ? $"Disc {parsed.DiscNumber}" : parsed.Title, parsed.DiscNumber, cancellationToken);
                file.QuickHash = await hashes.ComputeQuickHashAsync(candidate.FullPath, candidate.Size, cancellationToken);
                file.LastSeen = started;
                file.Status = FileStatus.Normal;
                await repository.UpsertFileAsync(file, cancellationToken);
                var collisions = await repository.FindQuickHashMatchesAsync(file.QuickHash, file.Size, file.Id, cancellationToken);
                if (collisions.Count > 0)
                {
                    var currentSha = await hashes.ComputeSha256Async(file.FullPath, cancellationToken);
                    await repository.SaveSha256Async(file.Id, currentSha, cancellationToken);
                    foreach (var collision in collisions)
                    {
                        var collisionSha = collision.Hashes.FirstOrDefault(x => x.Algorithm == "SHA256")?.Hash;
                        if (collisionSha is null)
                        {
                            collisionSha = await hashes.ComputeSha256Async(collision.FullPath, cancellationToken);
                            await repository.SaveSha256Async(collision.Id, collisionSha, cancellationToken);
                        }
                        if (collisionSha == currentSha) await repository.MarkExactDuplicatesAsync(currentSha, cancellationToken);
                    }
                }
                if (existing is null) counts.Added++; else counts.Changed++;
                counts.Processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            { counts.AddError($"{candidate.FullPath}: {ex.Message}"); logger.LogWarning(ex, "Could not index {Path}", candidate.FullPath); }
            Report(candidate.FullPath);
        }
        counts.Missing = await repository.MarkUnseenFilesMissingAsync(location.Id, started, cancellationToken);
        await repository.CompleteScanAsync(location.Id, started, counts.Discovered, cancellationToken);
        Report(location.Path, true);
        logger.LogInformation("Scan completed for {Location}: {Discovered} discovered, {Processed} processed, {Skipped} skipped, {Errors} errors", location.Path, counts.Discovered, counts.Processed, counts.Skipped, counts.Errors.Count);
        return counts.Result();

        void Report(string current, bool force = false)
        {
            if (progress is null || (!force && progressClock.ElapsedMilliseconds < 250)) return;
            progress.Report(new ScanProgress(counts.Discovered, counts.Processed, counts.Skipped, counts.Added, counts.Changed, counts.Missing, current));
            progressClock.Restart();
        }
    }

    private sealed class Counters
    {
        public long Discovered, Processed, Skipped, Added, Changed, Missing;
        public List<string> Errors { get; } = [];
        public void AddError(string message) { if (Errors.Count < 1000) Errors.Add(message); }
        public void Add(ScanResult r) { Discovered += r.Discovered; Processed += r.Processed; Skipped += r.Skipped; Added += r.Added; Changed += r.Changed; Missing += r.Missing; Errors.AddRange(r.Errors); }
        public ScanResult Result() => new(Discovered, Processed, Skipped, Added, Changed, Missing, Errors);
    }
}
