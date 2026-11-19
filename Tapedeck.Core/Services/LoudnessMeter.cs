using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tapedeck.Core.Services;

/// <summary>
/// Measures the integrated loudness of an audio file (EBU R 128) with the bundled ffmpeg and turns it into
/// the Opus track gain. YouTube uploads differ a lot in loudness, music videos are often much louder than
/// "Topic" uploads; players on the phone level them out with the gain tag.
/// </summary>
public sealed partial class LoudnessMeter(IToolHost tools)
{
    /// <summary>The reference loudness of Opus gain tags (RFC 7845, section 5.2.1).</summary>
    public const double R128_REFERENCE_LUFS = -23;

    /// <summary>ReplayGain 2.0 levels to -18 LUFS, 5 dB louder than R128.</summary>
    public const double REPLAYGAIN_REFERENCE_LUFS = -18;

    /// <summary>
    /// Level the app's own player brings every track to: the ReplayGain reference, about where players on the
    /// phone end up with the track gain. Loud YouTube uploads (-7 to -10 LUFS) are turned down by about 10 dB.
    /// </summary>
    public const double PLAYBACK_TARGET_LUFS = -18;

    // a quiet track is raised at most this much; OpenAL Soft's output limiter catches the peaks
    private const double MAX_PLAYBACK_BOOST_DB = 6;

    /// <summary>ffmpeg can run; without it nothing is measured.</summary>
    public bool IsAvailable => tools.HasFFmpeg;

    /// <summary>
    /// Decodes the whole file once with ffmpeg's <c>ebur128</c> filter, which takes well under a second
    /// for a song.
    /// </summary>
    /// <returns>The integrated loudness in LUFS.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg is missing, failed, or printed no summary.</exception>
    public async Task<double> MeasureAsync(string filePath, CancellationToken cancellation = default)
    {
        var psi = tools.FFmpeg();
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-nostats", "-nostdin",
                     "-i", filePath,
                     "-map", "0:a:0",
                     "-af", "ebur128=framelog=quiet",
                     "-f", "null", "-",
                 })
            psi.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"ffmpeg could not be started: {ex.Message}", ex);
        }
        if (process is null)
            throw new InvalidOperationException("ffmpeg could not be started.");

        using (process)
        {
            // the summary goes to stderr; stdout stays empty but is read so ffmpeg never blocks on it
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var errors = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await process.WaitForExitAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            await output;
            var log = await errors;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {LastLine(log)}");
            return ParseIntegrated(log)
                   ?? throw new InvalidOperationException($"ffmpeg printed no loudness summary: {LastLine(log)}");
        }
    }

    /// <summary>
    /// The integrated loudness from the summary ffmpeg's <c>ebur128</c> filter prints at the end
    /// (<c>I: -9.8 LUFS</c>); <c>null</c> without a summary.
    /// </summary>
    public static double? ParseIntegrated(string log)
    {
        var summary = log.LastIndexOf("Integrated loudness:", StringComparison.Ordinal);
        if (summary < 0)
            return null;
        var match = IntegratedPattern().Match(log, summary);
        return match.Success
            ? double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// The value of <c>R128_TRACK_GAIN</c>: the gain to the reference of -23 LUFS in 1/256 dB,
    /// limited to what the tag can hold (a signed 16 bit number).
    /// </summary>
    public static int TrackGain(double lufs)
    {
        var steps = Math.Round((R128_REFERENCE_LUFS - lufs) * 256, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(steps, short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// The value of <c>REPLAYGAIN_TRACK_GAIN</c>: the gain to the ReplayGain reference of -18 LUFS in decibels,
    /// e.g. <c>-5.23 dB</c>.
    /// </summary>
    public static string ReplayGain(double lufs) =>
        (REPLAYGAIN_REFERENCE_LUFS - lufs).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " dB";

    /// <summary>
    /// Linear volume factor for playing a track with the given loudness at <see cref="PLAYBACK_TARGET_LUFS"/>;
    /// 1 without a measurement or for silence.
    /// </summary>
    public static float PlaybackGain(double? lufs)
    {
        if (lufs is not { } value || value <= -70)
            return 1;
        var decibels = Math.Min(PLAYBACK_TARGET_LUFS - value, MAX_PLAYBACK_BOOST_DB);
        return (float)Math.Pow(10, decibels / 20);
    }

    private static string LastLine(string log) =>
        log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    [GeneratedRegex(@"I:\s+(-?\d+(?:\.\d+)?)\s+LUFS")]
    private static partial Regex IntegratedPattern();
}
