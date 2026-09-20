using RomManager.Core.Models;
using RomManager.Core.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RomManager.Core.Scanner;

public sealed class LibraryScanner(IFileSystem fileSystem, IFormatIdentifier formats, IArchiveInspector archives, IFileNameParser parser, IHashService hashes, ILibraryRepository repository, ILogger<LibraryScanner> logger) : ILibraryScanner
{
    private const int UnchangedBatchSize = 400;

    public async Task<ScanResult> ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var totals = new Counters();
        var snapshots = new Dictionary<string, ExistingFileSnapshot>(await repository.GetFileSnapshotsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        foreach (var location in await repository.GetScanLocationsAsync(true, cancellationToken))
            totals.Add(await ScanLocationCoreAsync(location, snapshots, progress, cancellationToken));
        await repository.RecalculatePreferredCopiesAsync(null, cancellationToken);
        return totals.Result();
    }

    public async Task<ScanResult> ScanLocationAsync(ScanLocation location, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, ExistingFileSnapshot>(await repository.GetFileSnapshotsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        var result = await ScanLocationCoreAsync(location, snapshots, progress, cancellationToken);
        await repository.RecalculatePreferredCopiesAsync(null, cancellationToken);
        return result;
    }

    private async Task<ScanResult> ScanLocationCoreAsync(ScanLocation location, Dictionary<string, ExistingFileSnapshot> snapshots, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!fileSystem.DirectoryExists(location.Path)) { logger.LogWarning("Scan location unavailable: {Location}", location.Path); return new ScanResult(0, 0, 0, 0, 0, 0, [$"Location unavailable: {location.Path}"]); }
        var started = DateTimeOffset.UtcNow;
        await repository.BeginScanAsync(location.Id, started, cancellationToken);
        logger.LogInformation("Scan started for {Location}; {KnownFiles} indexed file snapshots are cached", location.Path, snapshots.Count);
        var counts = new Counters();
        var scanClock = Stopwatch.StartNew();
        var progressClock = Stopwatch.StartNew();
        var unchangedBatch = new List<long>(UnchangedBatchSize);
        await foreach (var candidate in fileSystem.EnumerateFilesAsync(location.Path, location.Recursive, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Discovered++;
            snapshots.TryGetValue(candidate.FullPath, out var snapshot);
            if (!formats.IsCandidate(candidate.Extension))
            {
                if (snapshot is not null) await QueueUnchangedAsync(snapshot.Id);
                counts.AddUnsupported(candidate.Extension); logger.LogDebug("Skipped unsupported file {Path} ({Extension})", candidate.FullPath, candidate.Extension); Report(candidate.FullPath); continue;
            }
            try
            {
                if (snapshot is not null && snapshot.Size == candidate.Size && snapshot.ModifiedDate == candidate.Modified)
                {
                    await QueueUnchangedAsync(snapshot.Id);
                    counts.Skipped++; counts.Unchanged++; logger.LogDebug("Skipped unchanged file {Path}", candidate.FullPath); Report(candidate.FullPath); continue;
                }

                var existing = snapshot is null ? null : await repository.FindFileByPathAsync(candidate.FullPath, cancellationToken);
                var identified = candidate.Extension is ".zip" or ".7z" or ".rar" ? await archives.IdentifyContentsAsync(candidate, cancellationToken) ?? formats.Identify(candidate) : formats.Identify(candidate);
                if (identified.System is null || identified.Format is null) { if (snapshot is not null) await QueueUnchangedAsync(snapshot.Id); counts.AddUnsupported(candidate.Extension); logger.LogDebug("Skipped unidentified file {Path} ({Reason})", candidate.FullPath, identified.Reason); Report(candidate.FullPath); continue; }
                var directoryTitle = candidate.IsDirectoryGame && candidate.IdentityFilePath is not null ? await TryReadDirectoryGameTitleAsync(candidate.IdentityFilePath, cancellationToken) : null;
                var parsed = parser.Parse(directoryTitle ?? candidate.FileName);
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
                file.IsDirectory = candidate.IsDirectoryGame;
                file.DetectionConfidence = identified.Confidence;
                file.DetectionReason = identified.Reason;
                file.IsDetectionManual = false;
                if (identified.Format.IsMultiFile || parsed.TrackNumber.HasValue || (candidate.Extension == ".bin" && identified.Format.FormatType == FileCategory.DiscImage))
                    file.FileGroupId = await repository.GetOrCreateFileGroupAsync(game.Id, parsed.DiscNumber.HasValue ? $"Disc {parsed.DiscNumber}" : parsed.Title, parsed.DiscNumber, cancellationToken);
                // A directory game's "quick hash" is a hash of its small PARAM.SFO identity file, not a
                // sample of the (potentially many-GB) directory tree - hashing directory contents wholesale
                // isn't practical during a routine scan, so exact-duplicate detection across directory
                // installs is not attempted in this pass.
                file.QuickHash = candidate.IsDirectoryGame && candidate.IdentityFilePath is not null
                    ? "paramsfo:" + await hashes.ComputeSha256Async(candidate.IdentityFilePath, cancellationToken)
                    : await hashes.ComputeQuickHashAsync(candidate.FullPath, candidate.Size, cancellationToken);
                file.LastSeen = started;
                file.Status = FileStatus.Normal;
                file.CatalogStatus = CatalogVerificationStatus.Unknown;
                file.CatalogSource = null;
                file.CatalogName = null;
                file.CatalogVerifiedAt = null;
                file.IsPreferred = false;
                file.PreferenceScore = 0;
                await repository.UpsertFileAsync(file, cancellationToken);
                snapshots[candidate.FullPath] = new ExistingFileSnapshot(file.Id, file.FullPath, file.Size, file.ModifiedDate, file.Status);
                if (!candidate.IsDirectoryGame)
                {
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
                }
                if (existing is null) counts.Added++; else counts.Changed++;
                counts.Processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                if (snapshot is not null) await QueueUnchangedAsync(snapshot.Id);
                counts.AddError($"{candidate.FullPath}: {ex.Message}"); logger.LogWarning(ex, "Could not index {Path}", candidate.FullPath);
            }
            Report(candidate.FullPath);
        }
        await FlushUnchangedAsync();
        counts.Missing = await repository.MarkUnseenFilesMissingAsync(location.Id, started, cancellationToken);
        await repository.CompleteScanAsync(location.Id, started, counts.Discovered, cancellationToken);
        Report(location.Path, true);
        var elapsed = scanClock.Elapsed;
        var rate = elapsed.TotalSeconds <= 0 ? counts.Discovered : counts.Discovered / elapsed.TotalSeconds;
        logger.LogInformation("Scan completed for {Location} in {Elapsed}: {Rate:N1} files/sec; {Discovered} discovered, {Processed} processed, {Skipped} skipped ({Unchanged} unchanged, {Unsupported} unsupported), {Errors} errors", location.Path, elapsed, rate, counts.Discovered, counts.Processed, counts.Skipped, counts.Unchanged, counts.Unsupported, counts.Errors.Count);
        if (counts.UnsupportedByExtension.Count > 0)
            logger.LogInformation("Unsupported extension summary for {Location}: {Extensions}", location.Path, string.Join(", ", counts.UnsupportedByExtension.OrderByDescending(x => x.Value).Select(x => $"{(string.IsNullOrEmpty(x.Key) ? "[no extension]" : x.Key)}={x.Value:N0}")));
        return counts.Result();

        async Task QueueUnchangedAsync(long id)
        {
            unchangedBatch.Add(id);
            if (unchangedBatch.Count >= UnchangedBatchSize) await FlushUnchangedAsync();
        }

        async Task FlushUnchangedAsync()
        {
            if (unchangedBatch.Count == 0) return;
            await repository.TouchUnchangedFilesAsync(unchangedBatch, started, cancellationToken);
            unchangedBatch.Clear();
        }

        void Report(string current, bool force = false)
        {
            if (progress is null || (!force && progressClock.ElapsedMilliseconds < 250)) return;
            progress.Report(new ScanProgress(counts.Discovered, counts.Processed, counts.Skipped, counts.Added, counts.Changed, counts.Missing, current));
            progressClock.Restart();
        }
    }

    private async Task<string?> TryReadDirectoryGameTitleAsync(string identityFilePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await fileSystem.OpenReadAsync(identityFilePath, cancellationToken);
            return RomManager.Core.Grouping.ParamSfoReader.TryReadTitle(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not read directory-game title from {Path}", identityFilePath);
            return null;
        }
    }

    private sealed class Counters
    {
        public long Discovered, Processed, Skipped, Added, Changed, Missing, Unchanged, Unsupported;
        public List<string> Errors { get; } = [];
        public Dictionary<string, long> UnsupportedByExtension { get; } = new(StringComparer.OrdinalIgnoreCase);
        public void AddError(string message) { if (Errors.Count < 1000) Errors.Add(message); }
        public void AddUnsupported(string extension) { Skipped++; Unsupported++; UnsupportedByExtension[extension] = UnsupportedByExtension.GetValueOrDefault(extension) + 1; }
        public void Add(ScanResult r) { Discovered += r.Discovered; Processed += r.Processed; Skipped += r.Skipped; Added += r.Added; Changed += r.Changed; Missing += r.Missing; Errors.AddRange(r.Errors); }
        public ScanResult Result() => new(Discovered, Processed, Skipped, Added, Changed, Missing, Errors);
    }
}
