using Tapedeck.Core.Services;

namespace Tapedeck.Tests;

public sealed class YtDlpDownloadTests
{
    [Fact]
    public void Arguments_ask_for_the_best_audio_as_opus_with_progress_and_the_final_file()
    {
        var args = YtDlpDownload.Arguments("https://www.youtube.com/watch?v=abc", "/tmp/abc.%(ext)s", "/tools/ffmpeg", "opus");

        Assert.Equal(["-f", "bestaudio/best"], args.Skip(IndexOf(args, "-f")).Take(2));
        Assert.Equal(["-x", "--audio-format", "opus"], args.Skip(IndexOf(args, "-x")).Take(3));
        Assert.Equal("/tools/ffmpeg", args[IndexOf(args, "--ffmpeg-location") + 1]);
        Assert.Equal("/tmp/abc.%(ext)s", args[IndexOf(args, "-o") + 1]);
        Assert.Contains("--no-playlist", args);
        // without it --print only simulates
        Assert.Contains("--no-simulate", args);
        Assert.Equal("after_move:tapedeck-file:%(filepath)s", args[IndexOf(args, "--print") + 1]);
        Assert.Equal("download:tapedeck-progress:%(progress._percent)s", args[IndexOf(args, "--progress-template") + 1]);
        // the URL comes last, after "--", so a URL starting with "-" is never read as an option
        Assert.Equal(["--", "https://www.youtube.com/watch?v=abc"], args.TakeLast(2));
    }

    [Fact]
    public void The_try_without_a_JS_runtime_takes_only_opus()
    {
        var args = YtDlpDownload.Arguments("https://www.youtube.com/watch?v=abc", "/tmp/abc.%(ext)s", "/tools/ffmpeg", "opus",
            YtDlpDownload.OPUS_AUDIO);

        Assert.Equal(["-f", "bestaudio[acodec=opus]"], args.Skip(IndexOf(args, "-f")).Take(2));
    }

    [Theory]
    [InlineData("ERROR: [youtube] abc: Requested format is not available. Use --list-formats for a list of available formats")]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age. This video may be inappropriate for some users.")]
    [InlineData("yt-dlp ended with exit code 1 and left no file.")]
    public void Errors_a_JS_runtime_may_solve_are_tried_again(string error)
    {
        Assert.True(YtDlpDownload.IsWorthRetrying(error));
    }

    [Theory]
    [InlineData("ERROR: [youtube] abc: Video unavailable. This video is no longer available")]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access to this video")]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot.")]
    [InlineData("ERROR: unable to download video data: HTTP Error 429: Too Many Requests")]
    public void Errors_a_JS_runtime_does_not_change_are_final(string error)
    {
        Assert.False(YtDlpDownload.IsWorthRetrying(error));
    }

    [Theory]
    [InlineData("tapedeck-progress:0", 0)]
    [InlineData("tapedeck-progress:42.5", 0.425)]
    [InlineData("tapedeck-progress:100", 1)]
    [InlineData("tapedeck-progress: 12.0%", 0.12)]
    [InlineData("[download] tapedeck-progress:50", 0.5)]
    public void Progress_lines_become_fractions(string line, double expected)
    {
        Assert.True(YtDlpDownload.TryParseProgress(line, out var fraction));
        Assert.Equal(expected, fraction, precision: 6);
    }

    [Theory]
    [InlineData("[download] Destination: /tmp/abc.webm")]
    [InlineData("tapedeck-progress:NA")]
    [InlineData("tapedeck-file:/tmp/abc.opus")]
    public void Other_lines_are_no_progress(string line)
    {
        Assert.False(YtDlpDownload.TryParseProgress(line, out _));
    }

    [Fact]
    public void The_file_line_gives_the_final_path()
    {
        Assert.True(YtDlpDownload.TryParseFile(@"tapedeck-file:C:\Users\me\AppData\Roaming\Tapedeck\tmp\abc.opus", out var path));
        Assert.Equal(@"C:\Users\me\AppData\Roaming\Tapedeck\tmp\abc.opus", path);

        Assert.False(YtDlpDownload.TryParseFile("[ExtractAudio] Destination: /tmp/abc.opus", out _));
        Assert.False(YtDlpDownload.TryParseFile("tapedeck-file:", out _));
    }

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == value)
                return i;
        }
        throw new Xunit.Sdk.XunitException($"{value} is missing in: {string.Join(' ', args)}");
    }
}
