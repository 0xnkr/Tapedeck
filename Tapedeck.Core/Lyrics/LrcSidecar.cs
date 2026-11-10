using System.Text;

namespace Tapedeck.Core.Lyrics;

/// <summary>
/// The <c>.lrc</c> file next to a song (<c>Artist - Title.lrc</c>), which phone players read for timed lyrics.
/// Only files Tapedeck wrote (<see cref="LrcText.OWNER_TAG"/>) are overwritten, moved or deleted; a <c>.lrc</c>
/// of the user's own stays as it is.
/// </summary>
public static class LrcSidecar
{
    public const string EXTENSION = ".lrc";

    // as the .m3u8 files: phones read UTF-8 without a BOM best
    private static readonly UTF8Encoding ENCODING = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The <c>.lrc</c> path for an audio file or a library-relative file name.</summary>
    public static string PathFor(string audioPath) => Path.ChangeExtension(audioPath, EXTENSION);

    /// <summary>Whether the file exists and Tapedeck wrote it.</summary>
    public static bool IsOwn(string lrcPath)
    {
        try
        {
            return File.Exists(lrcPath) && LrcText.IsOwn(File.ReadLines(lrcPath, ENCODING).Take(5));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Whether a file of the user's own sits where the song's <c>.lrc</c> would go.</summary>
    public static bool IsForeign(string audioPath) => File.Exists(PathFor(audioPath)) && !IsOwn(PathFor(audioPath));

    /// <summary>Writes the timed lyrics next to the song, unless the user has a <c>.lrc</c> of their own there.</summary>
    /// <returns>Whether the file was written.</returns>
    public static bool Write(string audioPath, string lrc)
    {
        var path = PathFor(audioPath);
        if (IsForeign(audioPath))
            return false;

        var content = LrcText.WithOwnerTag(lrc).Replace("\n", "\r\n");
        // unchanged content keeps the file date, so syncing to the phone skips it
        if (File.Exists(path) && File.ReadAllText(path, ENCODING) == content)
            return true;
        File.WriteAllText(path, content, ENCODING);
        return true;
    }

    /// <summary>Deletes the song's <c>.lrc</c> if Tapedeck wrote it.</summary>
    public static void DeleteOwn(string audioPath)
    {
        var path = PathFor(audioPath);
        if (IsOwn(path))
            File.Delete(path);
    }

    /// <summary>
    /// Moves the song's own <c>.lrc</c> along with the song. When the user has a <c>.lrc</c> of their own at the new
    /// name, the old one is deleted instead of left behind under a name no song has.
    /// </summary>
    public static void MoveOwn(string fromAudioPath, string toAudioPath)
    {
        var from = PathFor(fromAudioPath);
        var to = PathFor(toAudioPath);
        if (from == to || !IsOwn(from))
            return;
        // a change of case only is the same file on Windows and may move
        if (File.Exists(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            File.Delete(from);
        else
            File.Move(from, to);
    }
}
