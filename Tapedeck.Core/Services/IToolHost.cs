using System.Diagnostics;

namespace Tapedeck.Core.Services;

/// <summary>
/// How yt-dlp and ffmpeg run on this platform. The desktop downloads them into the tools folder
/// (<see cref="ToolService"/>); a phone ships them inside the app and starts them through its Python launcher.
/// Callers take a prepared <see cref="ProcessStartInfo"/>, add their own arguments and start it.
/// </summary>
public interface IToolHost
{
    /// <summary>Everything is in place, so <see cref="SetupAsync"/> has nothing to do.</summary>
    bool IsReady { get; }

    /// <summary>Gets the tools ready once: downloads or unpacks what is missing, leaves the rest alone.</summary>
    Task SetupAsync();

    /// <summary>
    /// yt-dlp with output and errors redirected and the JS runtime already passed; the caller adds its arguments.
    /// </summary>
    /// <param name="withJsRuntime">
    /// <c>false</c> runs yt-dlp without any JS runtime: YouTube then answers only through clients without the JS
    /// challenge, which yt-dlp calls deprecated, but which is much faster where the runtime is slow.
    /// </param>
    ProcessStartInfo YtDlp(bool withJsRuntime = true);

    /// <summary>
    /// The JS runtime takes long for YouTube's challenge (QuickJS on a phone: ~20 s per video), so downloads first try
    /// without it and use it only when that fails.
    /// </summary>
    bool PrefersWithoutJsRuntime { get; }

    /// <summary>ffmpeg with output and errors redirected; the caller adds its arguments.</summary>
    ProcessStartInfo FFmpeg();

    /// <summary>ffmpeg can run; without it nothing is measured and no cover is cut.</summary>
    bool HasFFmpeg { get; }

    /// <summary>The ffmpeg executable, for yt-dlp's <c>--ffmpeg-location</c>.</summary>
    string FFmpegPath { get; }

    /// <summary>Name of the JS runtime yt-dlp uses for YouTube, for the settings (<c>Deno</c>).</summary>
    string JsRuntimeName { get; }

    /// <summary><c>yt-dlp -U</c>. Without regular updates yt-dlp breaks whenever YouTube changes.</summary>
    /// <returns>What yt-dlp printed.</returns>
    /// <exception cref="InvalidOperationException">yt-dlp could not update itself.</exception>
    Task<string> UpdateYtDlpAsync();

    /// <summary>Installed versions; <c>null</c> for a tool that is missing or does not start.</summary>
    Task<ToolVersions> GetVersionsAsync();
}

/// <summary>Versions of the tools; <c>null</c> when a tool is missing.</summary>
public sealed record ToolVersions(string? YtDlp, string? FFmpeg, string? JsRuntime);
