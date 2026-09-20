using System.Text;
using RomManager.Core.Grouping;

namespace RomManager.Tests.GroupingTests;

public sealed class ParamSfoReaderTests
{
    private static MemoryStream BuildSfo(string key, string value)
    {
        var keyBytes = Encoding.ASCII.GetBytes(key + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value);
        const int headerSize = 20;
        const int entrySize = 16;
        var keyTableStart = headerSize + entrySize;
        var dataTableStart = keyTableStart + keyBytes.Length;

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(0x46535000u); // magic ("\0PSF" little-endian)
        writer.Write(0x00000101u); // version
        writer.Write((uint)keyTableStart);
        writer.Write((uint)dataTableStart);
        writer.Write(1u); // entry count

        writer.Write((ushort)0); // key offset
        writer.Write((ushort)0x0204); // UTF-8 string format
        writer.Write((uint)valueBytes.Length); // data length
        writer.Write((uint)valueBytes.Length); // data max length
        writer.Write((uint)0); // data offset

        writer.Write(keyBytes);
        writer.Write(valueBytes);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void TryReadTitle_ExtractsTheTitleFieldFromAWellFormedSfo()
    {
        using var stream = BuildSfo("TITLE", "Army of Two (TM)");
        Assert.Equal("Army of Two (TM)", ParamSfoReader.TryReadTitle(stream));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullWhenTheTitleKeyIsAbsent()
    {
        using var stream = BuildSfo("APP_VER", "01.00");
        Assert.Null(ParamSfoReader.TryReadTitle(stream));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullForAStreamWithTheWrongMagic()
    {
        using var stream = new MemoryStream([0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Null(ParamSfoReader.TryReadTitle(stream));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullForATruncatedStreamInsteadOfThrowing()
    {
        using var full = BuildSfo("TITLE", "Army of Two");
        using var truncated = new MemoryStream(full.ToArray()[..10]);
        Assert.Null(ParamSfoReader.TryReadTitle(truncated));
    }
}
