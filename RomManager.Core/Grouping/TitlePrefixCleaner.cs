using System.Text.RegularExpressions;

namespace RomManager.Core.Grouping;

public static partial class TitlePrefixCleaner
{
    // Requires a *zero-padded* leading number ("0002 ", "001 - ") rather than any leading digits, because a
    // real title that happens to start with a number ("1080 Snowboarding") is never zero-padded — a catalog
    // ranking prefix is. This keeps the false-positive rate low enough to be useful even before human review.
    [GeneratedRegex(@"^0\d{2,5}[\s\-]+")]
    private static partial Regex RankPrefix();

    public static string? TryStripRankPrefix(string title)
    {
        var match = RankPrefix().Match(title);
        if (!match.Success) return null;
        var remainder = title[match.Length..].Trim();
        return remainder.Length == 0 ? null : remainder;
    }

    [GeneratedRegex(@"\s*[\(\[][^()\[\]]*[\)\]]\s*$")]
    private static partial Regex TrailingTag();

    // Repeatedly strips a trailing "(...)"/"[...]" group, e.g. "Dragon Quest III (Japan) (Rev 1)" becomes
    // "Dragon Quest III" — used to turn a verified catalog name (which still carries its region/version tags)
    // into a clean display title, since region is shown separately elsewhere in the app.
    public static string StripTrailingTags(string title)
    {
        var result = title;
        while (true)
        {
            var stripped = TrailingTag().Replace(result, "").TrimEnd();
            if (stripped == result || stripped.Length == 0) break;
            result = stripped;
        }
        return result;
    }
}
