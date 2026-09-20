using RomManager.Infrastructure.Catalog;

namespace RomManager.Tests.ScannerTests;

public sealed class CatalogDatParserTests
{
    [Fact]
    public void ParsesClrMameProRomHashes()
    {
        const string dat = """
            clrmamepro ( name "Test" )
            game (
                name "Asteroids (USA)"
                rom ( name "Asteroids.a26" size 8192 crc 0a2f8288 md5 8cf0 sha1 1cb8f057acad6dc65fe07d3202088ff4ae355cd )
            )
            """;

        var entry = Assert.Single(ClrMameProDatParser.Parse(dat));
        Assert.Equal("Asteroids (USA)", entry.Name);
        Assert.Equal(8192, entry.Size);
        Assert.Equal("0A2F8288", entry.Crc32);
        Assert.Equal("1CB8F057ACAD6DC65FE07D3202088FF4AE355CD", entry.Sha1);
    }

    [Fact]
    public void ParsesLogiqxXmlDat()
    {
        const string dat = """<?xml version="1.0"?><datafile><game name="Test Game"><rom name="test.rom" size="4" crc="12345678" sha1="001122" /></game></datafile>""";
        var entry = Assert.Single(ClrMameProDatParser.Parse(dat));
        Assert.Equal("Test Game", entry.Name);
        Assert.Equal("test.rom", entry.RomName);
        Assert.Equal("001122", entry.Sha1);
    }
}
