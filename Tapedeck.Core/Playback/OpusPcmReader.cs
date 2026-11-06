using Concentus;
using Concentus.Oggfile;

namespace Tapedeck.Core.Playback;

/// <summary>
/// Reads an Ogg Opus file (the library's <c>.opus</c>) as 16-bit stereo PCM at 48 kHz, in blocks for streaming.
/// Pure C# (Concentus), so it works on every platform and needs no audio device.
/// </summary>
public sealed class OpusPcmReader : IDisposable
{
    /// <summary>Opus always decodes at 48 kHz.</summary>
    public const int SAMPLE_RATE = 48000;

    /// <summary>Mono files are decoded to stereo too, so the output never changes format.</summary>
    public const int CHANNELS = 2;

    private readonly FileStream file;
    private readonly OpusOggReadStream ogg;
    // the rest of a packet cut by Seek, returned first by the next Read
    private short[]? pending;

    /// <exception cref="InvalidDataException">The file is no readable Ogg Opus file.</exception>
    public OpusPcmReader(string path)
    {
        // read sharing, so the library can still be scanned while a track plays
        this.file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            OpusCodecFactory.AttemptToUseNativeLibrary = false;
            this.ogg = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(SAMPLE_RATE, CHANNELS), this.file);
            if (!this.ogg.HasNextPacket)
                throw new InvalidDataException(this.ogg.LastError ?? $"No Opus audio in {Path.GetFileName(path)}.");
        }
        catch
        {
            this.file.Dispose();
            throw;
        }
    }

    /// <summary>Length of the file; <see cref="TimeSpan.Zero"/> when the file does not tell.</summary>
    public TimeSpan Duration => this.ogg.TotalTime;

    public bool CanSeek => this.ogg.CanSeek;

    /// <summary>
    /// The next PCM block, interleaved left/right, with at least <paramref name="minSamples"/> values
    /// unless the file ends first; <c>null</c> at the end.
    /// </summary>
    public short[]? Read(int minSamples)
    {
        var parts = new List<short[]>();
        var count = 0;
        if (this.pending is { } rest)
        {
            parts.Add(rest);
            count = rest.Length;
            this.pending = null;
        }

        while (count < minSamples && this.ogg.HasNextPacket)
        {
            // a packet that cannot be decoded is skipped, like a scratch on a record
            if (this.ogg.DecodeNextPacket() is { Length: > 0 } packet)
            {
                parts.Add(packet);
                count += packet.Length;
            }
        }

        if (count == 0)
            return null;
        if (parts.Count == 1)
            return parts[0];

        var block = new short[count];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, block, offset, part.Length);
            offset += part.Length;
        }
        return block;
    }

    /// <summary>Continues reading at <paramref name="position"/>, clamped to the file.</summary>
    /// <returns>Where reading continues.</returns>
    public TimeSpan Seek(TimeSpan position)
    {
        if (!this.ogg.CanSeek)
            return TimeSpan.Zero;
        var duration = this.Duration;
        var target = position < TimeSpan.Zero ? TimeSpan.Zero
            : duration > TimeSpan.Zero && position > duration ? duration
            : position;

        // Ogg jumps to the start of a page, which can be a second early; decode and drop the samples up to the target
        this.pending = null;
        this.ogg.SeekTo(target);
        var skip = (long)((target - this.ogg.CurrentTime).TotalSeconds * SAMPLE_RATE) * CHANNELS;
        while (skip > 0 && this.ogg.HasNextPacket)
        {
            if (this.ogg.DecodeNextPacket() is not { Length: > 0 } packet)
                continue;
            if (packet.Length <= skip)
            {
                skip -= packet.Length;
                continue;
            }
            this.pending = packet[(int)skip..];
            skip = 0;
        }
        return target;
    }

    public void Dispose() => this.file.Dispose();
}
