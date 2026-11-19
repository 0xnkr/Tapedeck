using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Tapedeck.Core.Services;

/// <summary>
/// Downloads the audio of one video with yt-dlp and converts it to Opus with ffmpeg.
/// Runs yt-dlp through the <see cref="IToolHost"/>, so it works wherever the host can start it.
/// </summary>
public static class YtDlpDownload
{
    // our own markers, so the lines are told apart from everything else yt-dlp prints
    public const string PROGRESS_PREFIX = "tapedeck-progress:";
    public const string FILE_PREFIX = "tapedeck-file:";

    /// <summary>The best audio, whatever its format: ffmpeg converts it.</summary>
    public const string ANY_AUDIO = "bestaudio/best";

    /// <summary>
    /// Only Opus audio (YouTube's format 251), for the try without a JS runtime: some formats may be missing then,
    /// and a worse one is not taken just because the runtime was left out.
    /// </summary>
    public const string OPUS_AUDIO = "bestaudio[acodec=opus]";

    // errors that a JS runtime does not change, so the slow second try is skipped
    private static readonly string[] FINAL_ERRORS = ["Video unavailable", "Private video", "This video has been removed"];

    /// <summary>
    /// The arguments for one download: the best audio, converted to <paramref name="audioFormat"/>,
    /// the progress as plain percentages and the final file once it is moved into place.
    /// </summary>
    /// <param name="outputTemplate">yt-dlp output template, e.g. <c>…/tmp/&lt;id&gt;.%(ext)s</c>.</param>
    /// <param name="format">yt-dlp format selection, <see cref="ANY_AUDIO"/> or <see cref="OPUS_AUDIO"/>.</param>
    public static IReadOnlyList<string> Arguments(string url, string outputTemplate, string ffmpegPath, string audioFormat,
        string format = ANY_AUDIO) =>
    [
        "--no-playlist",
        "-f", format,
        "-x", "--audio-format", audioFormat,
        "--ffmpeg-location", ffmpegPath,
        "-o", outputTemplate,
        // --print alone would only simulate and keep quiet; the file and the progress are wanted anyway
        "--no-simulate",
        "--print", $"after_move:{FILE_PREFIX}%(filepath)s",
        "--progress", "--newline",
        "--progress-template", $"download:{PROGRESS_PREFIX}%(progress._percent)s",
        "--", url,
    ];

    /// <summary>A progress line as a fraction from 0 to 1.</summary>
    public static bool TryParseProgress(string line, out double fraction)
    {
        fraction = 0;
        var start = line.IndexOf(PROGRESS_PREFIX, StringComparison.Ordinal);
        if (start < 0)
            return false;
        var value = line[(start + PROGRESS_PREFIX.Length)..].Trim().TrimEnd('%');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return false;
        fraction = Math.Clamp(percent / 100, 0, 1);
        return true;
    }

    /// <summary>The line with the final file.</summary>
    public static bool TryParseFile(string line, out string path)
    {
        path = string.Empty;
        if (!line.StartsWith(FILE_PREFIX, StringComparison.Ordinal))
            return false;
        path = line[FILE_PREFIX.Length..].Trim();
        return path.Length > 0;
    }

    /// <summary>
    /// Downloads and converts the audio. Where the host's JS runtime is slow, first without it; only when that fails
    /// (no Opus format, or YouTube insists on the challenge) again with it.
    /// </summary>
    /// <returns>The converted file.</returns>
    /// <exception cref="InvalidOperationException">yt-dlp failed or left no file; the message has its errors.</exception>
    public static async Task<string> RunAsync(IToolHost tools, string url, string outputTemplate, string audioFormat,
        IProgress<double>? progress, CancellationToken ct, ILogger? log = null)
    {
        if (!tools.PrefersWithoutJsRuntime)
            return await RunOnceAsync(tools.YtDlp(), tools, url, outputTemplate, audioFormat, ANY_AUDIO, progress, ct);

        var watch = Stopwatch.StartNew();
        try
        {
            return await RunOnceAsync(tools.YtDlp(withJsRuntime: false), tools, url, outputTemplate, audioFormat, OPUS_AUDIO,
                progress, ct);
        }
        catch (InvalidOperationException ex) when (IsWorthRetrying(ex.Message))
        {
            log?.LogWarning("Download of {Url} without a JS runtime failed after {Seconds:0.0} s, trying with it: {Error}",
                url, watch.Elapsed.TotalSeconds, ex.Message);
            progress?.Report(0);
            return await RunOnceAsync(tools.YtDlp(), tools, url, outputTemplate, audioFormat, ANY_AUDIO, progress, ct);
        }
    }

    /// <summary>Whether a try with the JS runtime may succeed where the one without it failed.</summary>
    public static bool IsWorthRetrying(string error) =>
        !MetadataService.IsThrottled(error) && !FINAL_ERRORS.Any(e => error.Contains(e, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> RunOnceAsync(ProcessStartInfo psi, IToolHost tools, string url, string outputTemplate,
        string audioFormat, string format, IProgress<double>? progress, CancellationToken ct)
    {
        psi.With(Arguments(url, outputTemplate, tools.FFmpegPath, audioFormat, format));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start yt-dlp.");

        string? file = null;
        var errors = new List<string>();
        // depending on its quiet mode yt-dlp puts the progress on either stream, so both are read the same way
        var output = ReadLinesAsync(process.StandardOutput, line =>
        {
            if (TryParseFile(line, out var path))
                file = path;
            else if (progress is not null && TryParseProgress(line, out var fraction))
                progress.Report(fraction);
        });
        var errorOutput = ReadLinesAsync(process.StandardError, line =>
        {
            if (progress is not null && TryParseProgress(line, out var fraction))
                progress.Report(fraction);
            else if (line.Length > 0)
                lock (errors)
                    errors.Add(line);
        });

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        await output;
        await errorOutput;

        if (process.ExitCode != 0 || file is null || !File.Exists(file))
        {
            var message = string.Join(Environment.NewLine, errors.Where(e => e.StartsWith("ERROR", StringComparison.Ordinal)));
            throw new InvalidOperationException(message.Length > 0 ? message
                : errors.Count > 0 ? string.Join(Environment.NewLine, errors)
                : $"yt-dlp ended with exit code {process.ExitCode} and left no file.");
        }
        return file;
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
            onLine(line);
    }
}
