using RomManager.Core.Grouping;

namespace RomManager.Tests.GroupingTests;

public sealed class FuzzyTitleMatcherTests
{
    [Fact]
    public void FlagsNearIdenticalTitlesAsCandidates()
    {
        var games = new[] { (1L, "sonic the hedgehog"), (2L, "sonik the hedgehog") };

        var pairs = FuzzyTitleMatcher.FindSimilarPairs(games);

        var pair = Assert.Single(pairs);
        Assert.Equal((1L, 2L), (pair.AId, pair.BId));
        Assert.True(pair.Similarity > 0.9);
    }

    [Fact]
    public void DoesNotFlagUnrelatedTitlesInDifferentBuckets()
    {
        var games = new[] { (1L, "sonic the hedgehog"), (2L, "streets of rage") };

        var pairs = FuzzyTitleMatcher.FindSimilarPairs(games);

        Assert.Empty(pairs);
    }

    [Fact]
    public void SkipsExactNormalizedTitleMatches()
    {
        // Exact matches are already merged deterministically during scanning (GetOrCreateGameAsync);
        // the fuzzy matcher only needs to surface near-misses, not identical titles.
        var games = new[] { (1L, "sonic the hedgehog"), (2L, "sonic the hedgehog") };

        var pairs = FuzzyTitleMatcher.FindSimilarPairs(games);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DoesNotFlagGenuinelyDifferentSequelTitles()
    {
        var games = new[] { (1L, "rockman 1"), (2L, "rockman 2"), (3L, "rockman 3") };

        var pairs = FuzzyTitleMatcher.FindSimilarPairs(games, minimumSimilarity: 0.85);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DoesNotFlagABaseTitleAgainstItsNumberedSequel()
    {
        // Regression: a title with no number and its "... 2" sequel are nearly identical text
        // but different games; a number present on only one side must still count as differing.
        var games = new[] { (1L, "poyon no dungeon room"), (2L, "poyon no dungeon room 2") };

        var pairs = FuzzyTitleMatcher.FindSimilarPairs(games, minimumSimilarity: 0.85);

        Assert.Empty(pairs);
    }
}
