using YoutubeDLSharp;

namespace Tapedeck.Core.Services;

/// <summary>
/// Manages yt-dlp, ffmpeg and Deno in the app's tools folder.
/// yt-dlp needs Deno as its JS runtime to fully extract YouTube.
/// </summary>
public sealed class ToolService(AppPaths paths)
{
    public string YtDlpPath => Path.Combine(paths.ToolsDir, Utils.YtDlpBinaryName);
    public string FFmpegPath => Path.Combine(paths.ToolsDir, Utils.FfmpegBinaryName);
    public string DenoPath => Path.Combine(paths.ToolsDir, OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    /// <summary>All binaries are present, so <see cref="EnsureToolsAsync"/> has nothing to download.</summary>
    public bool HasAllTools => File.Exists(YtDlpPath) && File.Exists(FFmpegPath) && File.Exists(DenoPath);

    /// <summary>Downloads missing binaries. Existing ones are left untouched.</summary>
    public async Task EnsureToolsAsync()
    {
        Directory.CreateDirectory(paths.ToolsDir);
        if (!File.Exists(YtDlpPath))
            await Utils.DownloadYtDlp(paths.ToolsDir);
        if (!File.Exists(FFmpegPath))
            await Utils.DownloadFFmpeg(paths.ToolsDir);
        if (!File.Exists(DenoPath))
            await Utils.DownloadDeno(paths.ToolsDir);
    }

    /// <summary><c>yt-dlp -U</c>. Without regular updates yt-dlp breaks whenever YouTube changes.</summary>
    public Task<string> UpdateYtDlpAsync() => CreateClient().RunUpdate();

    public YoutubeDL CreateClient() => new()
    {
        YoutubeDLPath = YtDlpPath,
        FFmpegPath = FFmpegPath,
        OutputFolder = paths.TempDir,
    };

    /// <summary>Arguments that make yt-dlp use the bundled Deno runtime.</summary>
    public IReadOnlyList<string> JsRuntimeArgs =>
        File.Exists(DenoPath) ? ["--js-runtimes", $"deno:{DenoPath}"] : [];
}
