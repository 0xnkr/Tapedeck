using System.Globalization;
using System.Text;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Playlists;

/// <summary>
/// Extended M3U (<c>.m3u8</c>, UTF-8) as Android players read it: one entry per track with its file name
/// relative to the playlist file, which lies in the flat library folder next to the audio files.
/// </summary>
public static class M3uWriter
{
    /// <summary>Marks files written by Tapedeck, so only those are ever replaced or deleted.</summary>
    public const string MARKER = "#TAPEDECK";

    public const string EXTENSION = ".m3u8";

    /// <summary>UTF-8 without BOM: some players read a BOM as part of the first line.</summary>
    public static readonly Encoding ENCODING = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The playlist's file name in the library, valid on FAT32/exFAT.</summary>
    public static string FileNameFor(string playlistName) => FileNaming.Sanitize(playlistName) + EXTENSION;

    /// <summary>
    /// The file content. Tracks whose file does not exist (<paramref name="fileExists"/>) are left out,
    /// so the player shows no dead entries.
    /// </summary>
    public static string Build(string playlistName, IEnumerable<TrackDisplay> tracks, Func<string, bool> fileExists) =>
        Build(playlistName, tracks, track => track.FilePath is { } file && fileExists(file) ? file : (string?)null);

    /// <summary>
    /// The file content with the entry of each track chosen by <paramref name="fileFor"/>, relative to the playlist;
    /// tracks it gives no file for are left out.
    /// </summary>
    public static string Build(string playlistName, IEnumerable<TrackDisplay> tracks, Func<TrackDisplay, string?> fileFor)
    {
        var text = new StringBuilder();
        text.Append("#EXTM3U\n");
        text.Append("#PLAYLIST:").Append(OneLine(playlistName)).Append('\n');
        text.Append(MARKER).Append('\n');
        foreach (var track in tracks)
        {
            if (fileFor(track) is not { } file)
                continue;
            var seconds = (track.DurationS ?? -1).ToString(CultureInfo.InvariantCulture);
            var label = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";
            text.Append("#EXTINF:").Append(seconds).Append(',').Append(OneLine(label)).Append('\n');
            text.Append(file).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Whether a file was written by Tapedeck (marker in the first lines).</summary>
    public static bool IsOwnFile(string path)
    {
        try
        {
            return File.ReadLines(path).Take(5).Any(line => line.Trim() == MARKER);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
