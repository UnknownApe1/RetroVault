using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using RomManager.Core.Models;
using RomManager.Core.Services;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;

namespace RomManager.Infrastructure.Catalog;

public sealed class LibretroCatalogVerificationService(IFileSystem fileSystem, ILibraryRepository repository, ILogger<LibretroCatalogVerificationService> logger) : ICatalogVerificationService
{
    private const string RawRoot = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static readonly IReadOnlyDictionary<string, CatalogDefinition> Catalogs = CreateCatalogs();

    public async Task<CatalogVerificationResult> VerifyAsync(string? systemKey, IProgress<CatalogVerificationProgress>? progress, CancellationToken ct)
    {
        var candidates = await repository.GetVerificationCandidatesAsync(systemKey, ct);
        var groups = candidates.GroupBy(x => x.SystemKey).Where(x => systemKey is null || x.Key == systemKey).ToArray();
        var systemsComplete = 0;
        long filesComplete = 0, verified = 0, noMatch = 0, unsupported = 0, errors = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (!Catalogs.TryGetValue(group.Key, out var definition))
            {
                var updates = group.Select(x => new CatalogVerificationUpdate(x.Id, CatalogVerificationStatus.Unsupported, "No catalog mapping", null, null, null, null)).ToArray();
                await repository.ApplyCatalogVerificationAsync(updates, ct);
                unsupported += updates.Length; filesComplete += updates.Length; systemsComplete++;
                progress?.Report(new(systemsComplete, groups.Length, filesComplete, candidates.Count, $"No catalog mapping for {group.Key}"));
                continue;
            }

            IReadOnlyList<CatalogEntry> entries;
            try
            {
                var dat = await GetCatalogAsync(definition, ct);
                entries = ClrMameProDatParser.Parse(dat);
                if (entries.Count == 0) throw new InvalidDataException($"{definition.FileName} contained no usable ROM records.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not load verification catalog for {SystemKey}", group.Key);
                var updates = group.Select(x => new CatalogVerificationUpdate(x.Id, CatalogVerificationStatus.Error, definition.DisplaySource, null, null, null, ex.Message)).ToArray();
                await repository.ApplyCatalogVerificationAsync(updates, ct);
                errors += updates.Length; filesComplete += updates.Length; systemsComplete++;
                progress?.Report(new(systemsComplete, groups.Length, filesComplete, candidates.Count, $"Catalog error for {group.Key}"));
                continue;
            }

            var sha1Index = entries.Where(x => x.Sha1 is not null).GroupBy(x => x.Sha1!, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var crcIndex = entries.Where(x => x.Crc32 is not null && x.Sha1 is null).GroupBy(x => (x.Size, x.Crc32!)).ToDictionary(x => x.Key, x => x.First());
            var pending = new List<CatalogVerificationUpdate>(100);
            foreach (var candidate in group)
            {
                ct.ThrowIfCancellationRequested();
                CatalogVerificationUpdate update;
                try
                {
                    var match = await MatchFileAsync(candidate, sha1Index, crcIndex, ct);
                    update = new(candidate.Id, match.Entry is null ? CatalogVerificationStatus.NoMatch : CatalogVerificationStatus.Verified,
                        definition.DisplaySource, match.Entry?.RomName, match.Sha1, match.Crc32, null);
                    if (match.Entry is null) noMatch++; else verified++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not catalog-verify {Path}", candidate.FullPath);
                    update = new(candidate.Id, CatalogVerificationStatus.Error, definition.DisplaySource, null, null, null, ex.Message);
                    errors++;
                }
                pending.Add(update); filesComplete++;
                if (pending.Count >= 100) { await repository.ApplyCatalogVerificationAsync(pending, ct); pending.Clear(); }
                progress?.Report(new(systemsComplete, groups.Length, filesComplete, candidates.Count, candidate.FullPath));
            }
            if (pending.Count > 0) await repository.ApplyCatalogVerificationAsync(pending, ct);
            systemsComplete++;
            progress?.Report(new(systemsComplete, groups.Length, filesComplete, candidates.Count, $"Verified {group.Key}"));
        }
        await repository.RecalculatePreferredCopiesAsync(systemKey, ct);
        return new(systemsComplete, filesComplete, verified, noMatch, unsupported, errors);
    }

    private async Task<MatchResult> MatchFileAsync(VerificationCandidate candidate, IReadOnlyDictionary<string, CatalogEntry> sha1Index, IReadOnlyDictionary<(long Size, string Crc), CatalogEntry> crcIndex, CancellationToken ct)
    {
        if (candidate.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await fileSystem.OpenReadAsync(candidate.FullPath, ct);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
            foreach (var entry in archive.Entries.Where(x => !string.IsNullOrEmpty(x.Name)))
            {
                var match = await TryMatchEntryAsync(candidate.SystemKey, entry.Name, entry.Length, entry.Open, sha1Index, crcIndex, ct);
                if (match is not null) return match;
            }
            return new(null, null, null);
        }

        if (candidate.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) || candidate.Extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await fileSystem.OpenReadAsync(candidate.FullPath, ct);
            using IArchive archive = candidate.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ? SevenZipArchive.Open(stream) : RarArchive.Open(stream);
            foreach (var entry in archive.Entries.Where(x => !x.IsDirectory && !string.IsNullOrEmpty(x.Key)))
            {
                var entryName = Path.GetFileName(entry.Key!);
                var match = await TryMatchEntryAsync(candidate.SystemKey, entryName, entry.Size, entry.OpenEntryStream, sha1Index, crcIndex, ct);
                if (match is not null) return match;
            }
            return new(null, null, null);
        }

        await using var file = await fileSystem.OpenReadAsync(candidate.FullPath, ct);
        var fileHashes = await HashStreamAsync(file, ct);
        var fileMatch = FindMatch(candidate.Size, fileHashes, sha1Index, crcIndex);
        if (fileMatch is not null) return new(fileMatch, fileHashes.Sha1, fileHashes.Crc32);
        var headerSkip = GetHeaderSkip(candidate.SystemKey, candidate.Extension, candidate.Size);
        if (headerSkip == 0) return new(null, fileHashes.Sha1, fileHashes.Crc32);
        await using var payloadFile = await fileSystem.OpenReadAsync(candidate.FullPath, ct);
        var payloadFileHashes = await HashStreamAsync(payloadFile, ct, headerSkip);
        return new(FindMatch(candidate.Size - headerSkip, payloadFileHashes, sha1Index, crcIndex), payloadFileHashes.Sha1, payloadFileHashes.Crc32);
    }

    private async Task<MatchResult?> TryMatchEntryAsync(string systemKey, string entryName, long entryLength, Func<Stream> openStream, IReadOnlyDictionary<string, CatalogEntry> sha1Index, IReadOnlyDictionary<(long Size, string Crc), CatalogEntry> crcIndex, CancellationToken ct)
    {
        await using (var entryStream = openStream())
        {
            var hashes = await HashStreamAsync(entryStream, ct);
            var match = FindMatch(entryLength, hashes, sha1Index, crcIndex);
            if (match is not null) return new(match, hashes.Sha1, hashes.Crc32);
        }
        var skip = GetHeaderSkip(systemKey, Path.GetExtension(entryName), entryLength);
        if (skip <= 0) return null;
        await using var payload = openStream();
        var payloadHashes = await HashStreamAsync(payload, ct, skip);
        var payloadMatch = FindMatch(entryLength - skip, payloadHashes, sha1Index, crcIndex);
        return payloadMatch is null ? null : new(payloadMatch, payloadHashes.Sha1, payloadHashes.Crc32);
    }

    private static CatalogEntry? FindMatch(long size, ContentHashes hashes, IReadOnlyDictionary<string, CatalogEntry> sha1Index, IReadOnlyDictionary<(long Size, string Crc), CatalogEntry> crcIndex)
    {
        if (sha1Index.TryGetValue(hashes.Sha1, out var sha1Match) && (sha1Match.Size == 0 || sha1Match.Size == size)) return sha1Match;
        return crcIndex.GetValueOrDefault((size, hashes.Crc32));
    }

    private static async Task<ContentHashes> HashStreamAsync(Stream stream, CancellationToken ct, int skipBytes = 0)
    {
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var crc = uint.MaxValue;
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (skipBytes > 0)
            {
                var skipped = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(skipBytes, buffer.Length)), ct);
                if (skipped == 0) break;
                skipBytes -= skipped;
            }
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sha1.AppendData(buffer, 0, read);
                for (var i = 0; i < read; i++) crc = CrcTable[(int)((crc ^ buffer[i]) & 0xff)] ^ (crc >> 8);
            }
            return new(Convert.ToHexString(sha1.GetHashAndReset()), (~crc).ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static int GetHeaderSkip(string systemKey, string extension, long size)
    {
        if (systemKey.Equals("NES", StringComparison.OrdinalIgnoreCase) && extension.Equals(".nes", StringComparison.OrdinalIgnoreCase) && size > 16 && (size - 16) % 8192 == 0) return 16;
        if (systemKey.Equals("FDS", StringComparison.OrdinalIgnoreCase) && extension.Equals(".fds", StringComparison.OrdinalIgnoreCase) && size > 16 && (size - 16) % 65500 == 0) return 16;
        if (systemKey.Equals("SNES", StringComparison.OrdinalIgnoreCase) && size > 512 && size % 1024 == 512) return 512;
        return 0;
    }

    private static async Task<string> GetCatalogAsync(CatalogDefinition definition, CancellationToken ct)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozziForged", "RomManager", "Catalogs");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, definition.Folder.Replace('/', '_') + "_" + definition.FileName);
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7)) return await File.ReadAllTextAsync(path, ct);
        var url = $"{RawRoot}/{definition.Folder}/{Uri.EscapeDataString(definition.FileName)}";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length > 100 * 1024 * 1024) throw new InvalidDataException("The catalog download exceeded the 100 MiB safety limit.");
            var text = await response.Content.ReadAsStringAsync(ct);
            var temporary = path + ".download";
            await File.WriteAllTextAsync(temporary, text, ct);
            File.Move(temporary, path, true);
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(path))
        {
            // A previously downloaded DAT is still useful when GitHub is temporarily unavailable.
            return await File.ReadAllTextAsync(path, ct);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CozziForged-RomManager/1.5");
        return client;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    private static IReadOnlyDictionary<string, CatalogDefinition> CreateCatalogs()
    {
        var noIntro = "no-intro";
        var redump = "redump";
        return new Dictionary<string, CatalogDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = new(noIntro, "Nintendo - Nintendo Entertainment System.dat", "Libretro / No-Intro"),
            ["FDS"] = new(noIntro, "Nintendo - Family Computer Disk System.dat", "Libretro / No-Intro"),
            ["GB"] = new(noIntro, "Nintendo - Game Boy.dat", "Libretro / No-Intro"),
            ["GBC"] = new(noIntro, "Nintendo - Game Boy Color.dat", "Libretro / No-Intro"),
            ["SNES"] = new(noIntro, "Nintendo - Super Nintendo Entertainment System.dat", "Libretro / No-Intro"),
            ["N64"] = new(noIntro, "Nintendo - Nintendo 64.dat", "Libretro / No-Intro"),
            ["N64DD"] = new(noIntro, "Nintendo - Nintendo 64DD.dat", "Libretro / No-Intro"),
            ["GBA"] = new(noIntro, "Nintendo - Game Boy Advance.dat", "Libretro / No-Intro"),
            ["NDS"] = new(noIntro, "Nintendo - Nintendo DS.dat", "Libretro / No-Intro"),
            ["DSI"] = new(noIntro, "Nintendo - Nintendo DSi.dat", "Libretro / No-Intro"),
            ["3DS"] = new(noIntro, "Nintendo - Nintendo 3DS.dat", "Libretro / No-Intro"),
            ["GC"] = new(redump, "Nintendo - GameCube.dat", "Libretro / Redump"),
            ["WII"] = new(redump, "Nintendo - Wii.dat", "Libretro / Redump"),
            ["GENESIS"] = new(noIntro, "Sega - Mega Drive - Genesis.dat", "Libretro / No-Intro"),
            ["SMS"] = new(noIntro, "Sega - Master System - Mark III.dat", "Libretro / No-Intro"),
            ["GG"] = new(noIntro, "Sega - Game Gear.dat", "Libretro / No-Intro"),
            ["SG1000"] = new(noIntro, "Sega - SG-1000.dat", "Libretro / No-Intro"),
            ["32X"] = new(noIntro, "Sega - 32X.dat", "Libretro / No-Intro"),
            ["SATURN"] = new(redump, "Sega - Saturn.dat", "Libretro / Redump"),
            ["DREAMCAST"] = new(redump, "Sega - Dreamcast.dat", "Libretro / Redump"),
            ["SEGACD"] = new(redump, "Sega - Mega-CD - Sega CD.dat", "Libretro / Redump"),
            ["PSX"] = new(redump, "Sony - PlayStation.dat", "Libretro / Redump"),
            ["PS2"] = new(redump, "Sony - PlayStation 2.dat", "Libretro / Redump"),
            ["PSP"] = new(redump, "Sony - PlayStation Portable.dat", "Libretro / Redump"),
            ["PS3"] = new(redump, "Sony - PlayStation 3.dat", "Libretro / Redump"),
            ["XBOX"] = new(redump, "Microsoft - Xbox.dat", "Libretro / Redump"),
            ["XBOX360"] = new(redump, "Microsoft - Xbox 360.dat", "Libretro / Redump"),
            ["ATARI2600"] = new(noIntro, "Atari - 2600.dat", "Libretro / No-Intro"),
            ["ATARI5200"] = new(noIntro, "Atari - 5200.dat", "Libretro / No-Intro"),
            ["ATARI7800"] = new(noIntro, "Atari - 7800.dat", "Libretro / No-Intro"),
            ["LYNX"] = new(noIntro, "Atari - Lynx.dat", "Libretro / No-Intro"),
            ["JAGUAR"] = new(noIntro, "Atari - Jaguar.dat", "Libretro / No-Intro"),
            ["JAGCD"] = new(redump, "Atari - Jaguar CD.dat", "Libretro / Redump"),
            ["PCE"] = new(noIntro, "NEC - PC Engine - TurboGrafx 16.dat", "Libretro / No-Intro"),
            ["SUPERGRAFX"] = new(noIntro, "NEC - PC Engine SuperGrafx.dat", "Libretro / No-Intro"),
            ["PCECD"] = new(redump, "NEC - PC Engine CD - TurboGrafx-CD.dat", "Libretro / Redump"),
            ["NGP"] = new(noIntro, "SNK - Neo Geo Pocket.dat", "Libretro / No-Intro"),
            ["NEOGEOCD"] = new(redump, "SNK - Neo Geo CD.dat", "Libretro / Redump"),
            ["3DO"] = new(redump, "The 3DO Company - 3DO.dat", "Libretro / Redump"),
            ["CDI"] = new(redump, "Philips - CD-i.dat", "Libretro / Redump"),
            ["CD32"] = new(redump, "Commodore - CD32.dat", "Libretro / Redump"),
            ["C64"] = new(noIntro, "Commodore - 64.dat", "Libretro / No-Intro"),
            ["AMIGA"] = new(noIntro, "Commodore - Amiga.dat", "Libretro / No-Intro"),
            ["MSX"] = new(noIntro, "Microsoft - MSX.dat", "Libretro / No-Intro"),
            ["ATARIST"] = new(noIntro, "Atari - ST.dat", "Libretro / No-Intro"),
            ["ATARI8"] = new(noIntro, "Atari - 8-bit Family.dat", "Libretro / No-Intro")
        };
    }

    private sealed record CatalogDefinition(string Folder, string FileName, string DisplaySource);
    private sealed record ContentHashes(string Sha1, string Crc32);
    private sealed record MatchResult(CatalogEntry? Entry, string? Sha1, string? Crc32);
}
