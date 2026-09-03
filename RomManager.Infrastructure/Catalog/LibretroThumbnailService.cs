using Microsoft.Extensions.Logging;
using RomManager.Core.Services;

namespace RomManager.Infrastructure.Catalog;

public sealed class LibretroThumbnailService(ILogger<LibretroThumbnailService> logger) : IThumbnailService
{
    private const string RawRoot = "https://raw.githubusercontent.com/libretro-thumbnails";
    private static readonly char[] InvalidNameCharacters = ['&', '*', '/', ':', '`', '<', '>', '?', '\\', '|', '"'];
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly IReadOnlyDictionary<string, string> SystemDisplayNames = CreateSystemDisplayNames();
    // Bounds simultaneous downloads when many list rows become visible at once (e.g. fast scrolling);
    // the local file/negative-cache checks above already short-circuit repeat lookups without hitting this.
    private static readonly SemaphoreSlim ConcurrencyGate = new(6);

    public async Task<string?> GetThumbnailPathAsync(string systemKey, string gameTitle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gameTitle) || !SystemDisplayNames.TryGetValue(systemKey, out var displayName)) return null;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozziForged", "RomManager", "Thumbnails", systemKey);
        Directory.CreateDirectory(root);
        var sanitized = SanitizeFileName(gameTitle);
        var localPath = Path.Combine(root, sanitized + ".png");
        var missingMarkerPath = localPath + ".missing";
        if (File.Exists(localPath)) return localPath;
        if (File.Exists(missingMarkerPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missingMarkerPath) < TimeSpan.FromDays(30)) return null;

        var repo = displayName.Replace(" - ", "_-_", StringComparison.Ordinal).Replace(' ', '_');
        var url = $"{RawRoot}/{repo}/master/Named_Boxarts/{Uri.EscapeDataString(sanitized)}.png";
        await ConcurrencyGate.WaitAsync(ct);
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) { await File.WriteAllTextAsync(missingMarkerPath, "", ct); return null; }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var temporary = localPath + ".download";
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, localPath, true);
            File.Delete(missingMarkerPath);
            return localPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not fetch thumbnail for {Title}", gameTitle);
            return null;
        }
        finally { ConcurrencyGate.Release(); }
    }

    private static string SanitizeFileName(string title)
    {
        var chars = title.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (Array.IndexOf(InvalidNameCharacters, chars[i]) >= 0) chars[i] = '_';
        return new string(chars);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CozziForged-RomManager/1.7");
        return client;
    }

    // Repo/file naming matches libretro-thumbnails exactly; kept as its own small table (rather than sharing
    // LibretroCatalogVerificationService's catalog map) so a thumbnail lookup failure can never affect DAT
    // verification, and vice versa.
    private static IReadOnlyDictionary<string, string> CreateSystemDisplayNames() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["NES"] = "Nintendo - Nintendo Entertainment System",
        ["FDS"] = "Nintendo - Family Computer Disk System",
        ["GB"] = "Nintendo - Game Boy",
        ["GBC"] = "Nintendo - Game Boy Color",
        ["SNES"] = "Nintendo - Super Nintendo Entertainment System",
        ["N64"] = "Nintendo - Nintendo 64",
        ["N64DD"] = "Nintendo - Nintendo 64DD",
        ["GBA"] = "Nintendo - Game Boy Advance",
        ["NDS"] = "Nintendo - Nintendo DS",
        ["DSI"] = "Nintendo - Nintendo DSi",
        ["3DS"] = "Nintendo - Nintendo 3DS",
        ["GC"] = "Nintendo - GameCube",
        ["WII"] = "Nintendo - Wii",
        ["GENESIS"] = "Sega - Mega Drive - Genesis",
        ["SMS"] = "Sega - Master System - Mark III",
        ["GG"] = "Sega - Game Gear",
        ["SG1000"] = "Sega - SG-1000",
        ["32X"] = "Sega - 32X",
        ["SATURN"] = "Sega - Saturn",
        ["DREAMCAST"] = "Sega - Dreamcast",
        ["SEGACD"] = "Sega - Mega-CD - Sega CD",
        ["PSX"] = "Sony - PlayStation",
        ["PS2"] = "Sony - PlayStation 2",
        ["PSP"] = "Sony - PlayStation Portable",
        ["PS3"] = "Sony - PlayStation 3",
        ["XBOX"] = "Microsoft - Xbox",
        ["XBOX360"] = "Microsoft - Xbox 360",
        ["ATARI2600"] = "Atari - 2600",
        ["ATARI5200"] = "Atari - 5200",
        ["ATARI7800"] = "Atari - 7800",
        ["LYNX"] = "Atari - Lynx",
        ["JAGUAR"] = "Atari - Jaguar",
        ["JAGCD"] = "Atari - Jaguar CD",
        ["PCE"] = "NEC - PC Engine - TurboGrafx 16",
        ["SUPERGRAFX"] = "NEC - PC Engine SuperGrafx",
        ["PCECD"] = "NEC - PC Engine CD - TurboGrafx-CD",
        ["NGP"] = "SNK - Neo Geo Pocket",
        ["NEOGEOCD"] = "SNK - Neo Geo CD",
        ["3DO"] = "The 3DO Company - 3DO",
        ["CDI"] = "Philips - CD-i",
        ["CD32"] = "Commodore - CD32",
        ["C64"] = "Commodore - 64",
        ["AMIGA"] = "Commodore - Amiga",
        ["MSX"] = "Microsoft - MSX",
        ["ATARIST"] = "Atari - ST",
        ["ATARI8"] = "Atari - 8-bit Family"
    };
}
