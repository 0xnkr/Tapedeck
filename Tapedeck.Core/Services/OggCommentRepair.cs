namespace Tapedeck.Core.Services;

/// <summary>
/// Works around a TagLib# bug in Ogg files: when the comment packet (the tags with the cover) is an exact
/// multiple of 255 bytes long, TagLib# leaves out the lacing value 0 that ends the packet. The packet then seems
/// to go on into the audio, and TagLib# itself can no longer open the file ("Value cannot be null (data)").
/// This adds the missing 0 to the packet's last page; the data itself is complete.
/// </summary>
public static class OggCommentRepair
{
    private const int HEADER_SIZE = 27;
    private const byte CONTINUED_PACKET = 0x01;
    private const int MAX_SEGMENTS = 255;

    private static readonly uint[] CRC_TABLE = BuildCrcTable();

    /// <summary>Repairs the file in place if its comment packet lacks the end; other files stay untouched.</summary>
    /// <returns>Whether the file was repaired.</returns>
    public static bool Repair(string path)
    {
        var data = File.ReadAllBytes(path);
        if (Repaired(data) is not { } fixedData)
            return false;

        var temp = path + ".tapedeck-tmp";
        File.WriteAllBytes(temp, fixedData);
        File.Move(temp, path, overwrite: true);
        return true;
    }

    /// <summary>The repaired file, or <c>null</c> when nothing is wrong or it is no Ogg file.</summary>
    public static byte[]? Repaired(byte[] data)
    {
        // the first page holds the identification header alone, the comment packet starts on the second
        if (ReadPage(data, 0) is not { } first || ReadPage(data, first.End) is not { } page)
            return null;

        while (page.LastLacing == MAX_SEGMENTS)
        {
            var next = ReadPage(data, page.End);
            if (next is { } following && following.Serial == page.Serial && (following.Flags & CONTINUED_PACKET) != 0)
            {
                page = following;
                continue;
            }

            // the packet ends here but says it goes on: add the closing lacing value 0
            if (page.SegmentCount == MAX_SEGMENTS)
                throw new InvalidOperationException("The comment packet lacks its end, and its last page is full.");
            return WithClosingLacing(data, page);
        }
        return null;
    }

    private static byte[] WithClosingLacing(byte[] data, Page page)
    {
        var result = new byte[data.Length + 1];
        var tableEnd = page.Start + HEADER_SIZE + page.SegmentCount;
        Array.Copy(data, result, tableEnd);
        result[tableEnd] = 0;
        Array.Copy(data, tableEnd, result, tableEnd + 1, data.Length - tableEnd);

        result[page.Start + 26] = (byte)(page.SegmentCount + 1);
        var pageLength = page.End + 1 - page.Start;
        // the checksum is computed with its own field set to 0
        Array.Clear(result, page.Start + 22, 4);
        BitConverter.TryWriteBytes(result.AsSpan(page.Start + 22, 4), Crc(result.AsSpan(page.Start, pageLength)));
        return result;
    }

    private static Page? ReadPage(byte[] data, int start)
    {
        if (start + HEADER_SIZE > data.Length || !data.AsSpan(start, 4).SequenceEqual("OggS"u8))
            return null;
        var segmentCount = data[start + 26];
        if (segmentCount == 0 || start + HEADER_SIZE + segmentCount > data.Length)
            return null;

        var lacing = data.AsSpan(start + HEADER_SIZE, segmentCount);
        var bodyLength = 0;
        foreach (var value in lacing)
            bodyLength += value;
        var end = start + HEADER_SIZE + segmentCount + bodyLength;
        if (end > data.Length)
            return null;

        return new Page(start, end, data[start + 5], BitConverter.ToUInt32(data, start + 14), segmentCount, lacing[^1]);
    }

    /// <summary>Ogg's CRC-32: polynomial 0x04C11DB7, not reflected, starting at 0.</summary>
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0;
        foreach (var b in bytes)
            crc = (crc << 8) ^ CRC_TABLE[(byte)(crc >> 24) ^ b];
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i << 24;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 0x80000000) != 0 ? (value << 1) ^ 0x04C11DB7 : value << 1;
            table[i] = value;
        }
        return table;
    }

    /// <param name="End">Offset right after the page.</param>
    private readonly record struct Page(int Start, int End, byte Flags, uint Serial, int SegmentCount, byte LastLacing);
}
