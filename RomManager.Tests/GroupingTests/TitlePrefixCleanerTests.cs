using RomManager.Core.Grouping;

namespace RomManager.Tests.GroupingTests;

public sealed class TitlePrefixCleanerTests
{
    [Theory]
    [InlineData("0002 Dragon Quest 1+2", "Dragon Quest 1+2")]
    [InlineData("001 - Super Mario Bros. 3", "Super Mario Bros. 3")]
    [InlineData("0001 - Kirby's Adventure", "Kirby's Adventure")]
    public void TryStripRankPrefix_RemovesAZeroPaddedRankPrefix(string title, string expected)
    {
        Assert.Equal(expected, TitlePrefixCleaner.TryStripRankPrefix(title));
    }

    [Theory]
    [InlineData("1080 Snowboarding")]
    [InlineData("Chrono Trigger")]
    [InlineData("64 de Hakken!! Tamagotchi Minna de Tamagotchi World")]
    public void TryStripRankPrefix_LeavesTitlesWithoutAZeroPaddedPrefixAlone(string title)
    {
        Assert.Null(TitlePrefixCleaner.TryStripRankPrefix(title));
    }

    [Fact]
    public void TryStripRankPrefix_ReturnsNullWhenStrippingWouldLeaveNothing()
    {
        Assert.Null(TitlePrefixCleaner.TryStripRankPrefix("0002 -   "));
    }

    [Theory]
    [InlineData("Dragon Quest III (Japan)", "Dragon Quest III")]
    [InlineData("Dragon Quest III (Japan) (Rev 1)", "Dragon Quest III")]
    [InlineData("Sonic the Hedgehog [Proto]", "Sonic the Hedgehog")]
    [InlineData("Chrono Trigger", "Chrono Trigger")]
    public void StripTrailingTags_RemovesTrailingParentheticalAndBracketedGroups(string title, string expected)
    {
        Assert.Equal(expected, TitlePrefixCleaner.StripTrailingTags(title));
    }
}
