using RomManager.Core.Grouping;
using RomManager.Core.Parsing;

namespace RomManager.Tests.GroupingTests;

public sealed class GameGroupingServiceTests
{
    [Fact]
    public void RegionsAndRevisionsGroupUnderSameSystemTitle()
    {
        var parser = new NoIntroFileNameParser(); var grouping = new GameGroupingService();
        var usa = grouping.CreateGroupingKey(parser.Parse("Super Mario World (USA).sfc"), "SNES");
        var japan = grouping.CreateGroupingKey(parser.Parse("Super Mario World (Japan) (Rev 1).smc"), "SNES");
        Assert.Equal(usa, japan);
    }

    [Fact]
    public void SameTitleOnDifferentSystemsDoesNotMerge()
    {
        var parsed = new NoIntroFileNameParser().Parse("Doom (USA).rom"); var grouping = new GameGroupingService();
        Assert.NotEqual(grouping.CreateGroupingKey(parsed, "SNES"), grouping.CreateGroupingKey(parsed, "GBA"));
    }
}
