namespace RomManager.Core.Grouping;

// Reads the "TITLE" field out of a PS3/PSP/Vita PARAM.SFO — the small binary metadata file every
// RPCS3-style decrypted game folder carries at its root (or under PS3_GAME/). This gives a folder-based
// install a real game title instead of whatever its folder happens to be named (often a product code like
// "BLUS30057-[Army of Two TM]"). Format: https://www.psdevwiki.com/ps3/PARAM.SFO
public static class ParamSfoReader
{
    private const uint Magic = 0x46535000; // "\0PSF" little-endian
    private const ushort Utf8StringFormat = 0x0204;

    public static string? TryReadTitle(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic) return null;
            reader.ReadUInt32(); // version
            var keyTableStart = reader.ReadUInt32();
            var dataTableStart = reader.ReadUInt32();
            var entryCount = reader.ReadUInt32();
            if (entryCount == 0 || entryCount > 1000) return null;

            // Read every index entry up front (sequential, no seeking) before resolving any key/data
            // offsets - resolving a key mid-loop would move the stream off the index table and corrupt
            // the position needed to read the next entry's fixed-size record.
            var entries = new (ushort KeyOffset, ushort DataFormat, uint DataLength, uint DataOffset)[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                var keyOffset = reader.ReadUInt16();
                var dataFormat = reader.ReadUInt16();
                var dataLength = reader.ReadUInt32();
                reader.ReadUInt32(); // data max length
                var dataOffset = reader.ReadUInt32();
                entries[i] = (keyOffset, dataFormat, dataLength, dataOffset);
            }

            foreach (var entry in entries)
            {
                if (entry.DataFormat != Utf8StringFormat) continue;
                stream.Seek((long)keyTableStart + entry.KeyOffset, SeekOrigin.Begin);
                if (ReadNullTerminatedAscii(stream) != "TITLE") continue;

                stream.Seek((long)dataTableStart + entry.DataOffset, SeekOrigin.Begin);
                var buffer = new byte[entry.DataLength];
                var read = stream.Read(buffer, 0, buffer.Length);
                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\0').Trim();
                return text.Length == 0 ? null : text;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static string ReadNullTerminatedAscii(Stream stream)
    {
        var bytes = new List<byte>(64);
        int b;
        while ((b = stream.ReadByte()) > 0) bytes.Add((byte)b);
        return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
    }
}
