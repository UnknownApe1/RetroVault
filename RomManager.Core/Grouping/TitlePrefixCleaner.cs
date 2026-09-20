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

    // Same shape as RankPrefix but without the leading-zero requirement, so it also catches a collection
    // numbered "100 ", "101 ", ... with no padding on the low end. This is deliberately over-eager (it would
    // also match a real title like "1080 Snowboarding"), so callers should only use it once they've confirmed,
    // via HasWidespreadRankPrefixConvention, that most titles in the same system share this leading-digit shape.
    [GeneratedRegex(@"^\d{2,5}[\s\-]+")]
    private static partial Regex AnyLeadingNumberPrefix();

    public static string? TryStripAnyLeadingNumberPrefix(string title)
    {
        var match = AnyLeadingNumberPrefix().Match(title);
        if (!match.Success) return null;
        var remainder = title[match.Length..].Trim();
        return remainder.Length == 0 ? null : remainder;
    }

    // A rank-prefix convention used by even one romset mixed into a system is enough to trust the pattern —
    // deliberately a raw count, not a share of the system's total games. A large system (e.g. thousands of
    // SNES entries merged from several sources) can have hundreds of consistently-prefixed titles from one
    // romset diluted well under any reasonable percentage by unrelated, unprefixed entries from another
    // source; requiring a share of the whole system let that dilution suppress the very convention it was
    // meant to detect. Five titles that all match "<2-5 digits><space or dash>Title" is already implausible
    // to occur by coincidence among real numbered titles ("1080 Snowboarding", "007").
    public static bool HasWidespreadRankPrefixConvention(int matchingTitles, int totalTitles) =>
        matchingTitles >= 5;

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
