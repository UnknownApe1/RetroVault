using RomManager.Core.Parsing;

namespace RomManager.Tests.ParserTests;

public sealed class NoIntroFileNameParserTests
{
    private readonly NoIntroFileNameParser parser = new();

    [Fact]
    public void ParsesRegionRevisionAndGoodDumpTag()
    {
        var result = parser.Parse("Super Mario World (USA) (Rev 1) [!].sfc");
        Assert.Equal("Super Mario World", result.Title);
        Assert.Contains("USA", result.Regions);
        Assert.Equal("Rev 1", result.Revision);
        Assert.Contains("!", result.Tags);
    }

    [Fact]
    public void ParsesMultipleRegionsAndLanguages()
    {
        var result = parser.Parse("Pokemon Emerald (USA, Europe) (En,Ja).gba");
        Assert.Equal(new[] { "USA", "Europe" }, result.Regions);
        Assert.Equal(new[] { "English", "Japanese" }, result.Languages);
    }

    [Theory]
    [InlineData("Final Fantasy VII (Disc 2).cue", 2)]
    [InlineData("Metal Gear Solid CD1.bin", 1)]
    public void ParsesDiscNumbers(string file, int expected) => Assert.Equal(expected, parser.Parse(file).DiscNumber.GetValueOrDefault());
}
