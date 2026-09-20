using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Core.Parsing;

public sealed partial class NoIntroFileNameParser : IFileNameParser
{
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    { ["En"] = "English", ["Ja"] = "Japanese", ["Fr"] = "French", ["De"] = "German", ["Es"] = "Spanish", ["It"] = "Italian", ["Pt"] = "Portuguese", ["Ko"] = "Korean", ["Zh"] = "Chinese" };
    private static readonly HashSet<string> Regions = new(StringComparer.OrdinalIgnoreCase)
    { "USA", "Europe", "Japan", "World", "Australia", "Asia", "Brazil", "Canada", "China", "France", "Germany", "Italy", "Korea", "Spain" };

    public ParsedFileName Parse(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        var tags = new List<string>();
        var regions = new List<string>();
        var languages = new List<string>();
        string? revision = null, version = null;
        int? disc = null, track = null;

        foreach (Match match in TagRegex().Matches(stem))
        {
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Regions.Contains(token)) regions.Add(token);
                else if (LanguageMap.TryGetValue(token, out var language)) languages.Add(language);
                else if (RevisionRegex().IsMatch(token)) revision = token;
                else if (VersionRegex().IsMatch(token)) version = token;
                else tags.Add(token);
            }
        }

        var discMatch = DiscRegex().Match(stem);
        if (discMatch.Success && int.TryParse(discMatch.Groups[1].Value, out var d)) disc = d;
        var trackMatch = TrackRegex().Match(stem);
        if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out var t)) track = t;

        var title = TagRegex().Replace(stem, " ");
        title = DiscRegex().Replace(title, " ");
        title = TrackRegex().Replace(title, " ");
        title = SeparatorRegex().Replace(title, " ").Trim(' ', '-', '.');
        if (string.IsNullOrWhiteSpace(title)) title = stem;

        return new ParsedFileName(title, Normalize(title), regions.Distinct().ToArray(), languages.Distinct().ToArray(), revision, version, disc, track, tags);
    }

    public static string Normalize(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    [GeneratedRegex(@"\(([^)]*)\)|\[([^]]*)\]", RegexOptions.Compiled)] private static partial Regex TagRegex();
    [GeneratedRegex(@"^(Rev(?:ision)?\s*[A-Z0-9.]+)$", RegexOptions.IgnoreCase)] private static partial Regex RevisionRegex();
    [GeneratedRegex(@"^(?:v|Version\s*)[0-9][\w.\-]*$", RegexOptions.IgnoreCase)] private static partial Regex VersionRegex();
    [GeneratedRegex(@"(?:Disc|Disk|CD)\s*0*([0-9]+)", RegexOptions.IgnoreCase)] private static partial Regex DiscRegex();
    [GeneratedRegex(@"Track\s*0*([0-9]+)", RegexOptions.IgnoreCase)] private static partial Regex TrackRegex();
    [GeneratedRegex(@"\s{2,}|\s+-\s*$", RegexOptions.Compiled)] private static partial Regex SeparatorRegex();
}
