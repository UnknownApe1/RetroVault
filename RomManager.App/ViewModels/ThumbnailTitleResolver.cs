namespace RomManager.App.ViewModels;

internal static class ThumbnailTitleResolver
{
    // A verified catalog name is the reliable match. Absent that, fall back to the parsed title with its
    // region tag reattached, since that is what libretro-thumbnails actually names files after
    // ("Banjo-Tooie (USA)") — the bare canonical title alone almost never matches.
    public static string? Resolve(string? catalogName, string? canonicalTitle, string? region)
    {
        if (!string.IsNullOrWhiteSpace(catalogName)) return catalogName;
        if (string.IsNullOrWhiteSpace(canonicalTitle)) return null;
        var primaryRegion = string.IsNullOrWhiteSpace(region) ? null : region.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(primaryRegion) ? canonicalTitle : $"{canonicalTitle} ({primaryRegion})";
    }
}
