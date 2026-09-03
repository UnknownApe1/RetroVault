namespace RomManager.Core.Grouping;

public static class FuzzyTitleMatcher
{
    // A bucket sharing a common first title word can still be huge for common words ("the", "super"). Skipping
    // oversized buckets keeps this a cheap, on-demand, click-to-run scan instead of a quadratic worst case.
    private const int MaxBucketSize = 150;

    public static IReadOnlyList<(long AId, long BId, double Similarity)> FindSimilarPairs(
        IReadOnlyList<(long Id, string NormalizedTitle)> games, double minimumSimilarity = 0.85, int maxResults = 300)
    {
        var results = new List<(long, long, double)>();
        foreach (var bucket in games.GroupBy(x => BucketKey(x.NormalizedTitle)))
        {
            var items = bucket.ToArray();
            if (items.Length < 2 || items.Length > MaxBucketSize) continue;
            for (var i = 0; i < items.Length; i++)
            {
                for (var j = i + 1; j < items.Length; j++)
                {
                    if (items[i].NormalizedTitle == items[j].NormalizedTitle) continue;
                    // The single most common false positive in a ROM library is a numbered sequel
                    // ("Rockman 1" vs "Rockman 2"): nearly identical text but a different game. Titles whose
                    // embedded numbers disagree are never treated as the same game, regardless of edit distance.
                    if (HasDifferingNumbers(items[i].NormalizedTitle, items[j].NormalizedTitle)) continue;
                    var similarity = Similarity(items[i].NormalizedTitle, items[j].NormalizedTitle);
                    if (similarity >= minimumSimilarity) results.Add((items[i].Id, items[j].Id, similarity));
                }
            }
        }
        return results.OrderByDescending(x => x.Item3).Take(maxResults).ToList();
    }

    // A base title against its numbered sequel ("Dungeon Room" vs "Dungeon Room 2") is just as much a false
    // positive as two numbered sequels of each other, so a number appearing on only one side counts as differing.
    private static bool HasDifferingNumbers(string a, string b) => !ExtractNumbers(a).SequenceEqual(ExtractNumbers(b));

    private static List<string> ExtractNumbers(string text)
    {
        var numbers = new List<string>();
        var current = "";
        foreach (var ch in text)
        {
            if (char.IsDigit(ch)) current += ch;
            else if (current.Length > 0) { numbers.Add(current); current = ""; }
        }
        if (current.Length > 0) numbers.Add(current);
        return numbers;
    }

    // Bucketing by exact first word would miss the common case of a typo in that very word (e.g. "Sonik" vs
    // "Sonic"), so instead this buckets by first character and a coarse length band, which still tolerates a
    // handful of character differences anywhere in the title while keeping candidate pairs bounded.
    private static string BucketKey(string normalized) => normalized.Length == 0 ? "" : $"{normalized[0]}-{normalized.Length / 4}";

    public static double Similarity(string a, string b)
    {
        var distance = LevenshteinDistance(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return maxLength == 0 ? 1 : 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) costs[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var previousDiagonal = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var previousDiagonalSaved = costs[j];
                costs[j] = a[i - 1] == b[j - 1] ? previousDiagonal : 1 + Math.Min(previousDiagonal, Math.Min(costs[j], costs[j - 1]));
                previousDiagonal = previousDiagonalSaved;
            }
        }
        return costs[b.Length];
    }
}
