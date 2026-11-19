using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using YoutubeDLSharp;

namespace Tapedeck.Core.Services;

/// <summary>
/// The desktop's tools: yt-dlp, ffmpeg and Deno downloaded into the app's tools folder.
/// yt-dlp needs Deno as its JS runtime to fully extract YouTube.
/// </summary>
public sealed class ToolService(AppPaths paths, ILogger<ToolService>? logger = null) : IToolHost
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public string YtDlpPath => Path.Combine(paths.ToolsDir, Utils.YtDlpBinaryName);
    public string FFmpegPath => Path.Combine(paths.ToolsDir, Utils.FfmpegBinaryName);
    public string DenoPath => Path.Combine(paths.ToolsDir, OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    public string JsRuntimeName => "Deno";

    public bool IsReady => File.Exists(this.YtDlpPath) && File.Exists(this.FFmpegPath) && File.Exists(this.DenoPath);

    public bool HasFFmpeg => File.Exists(this.FFmpegPath);

    /// <summary>Downloads missing binaries. Existing ones are left untouched.</summary>
    public async Task SetupAsync()
    {
        Directory.CreateDirectory(paths.ToolsDir);
        if (!File.Exists(this.YtDlpPath))
        {
            this.log.LogInformation("Downloading yt-dlp into {ToolsDir}", paths.ToolsDir);
            await Utils.DownloadYtDlp(paths.ToolsDir);
        }
        if (!File.Exists(this.FFmpegPath))
        {
            this.log.LogInformation("Downloading ffmpeg into {ToolsDir}", paths.ToolsDir);
            await Utils.DownloadFFmpeg(paths.ToolsDir);
        }
        if (!File.Exists(this.DenoPath))
        {
            this.log.LogInformation("Downloading Deno into {ToolsDir}", paths.ToolsDir);
            await Utils.DownloadDeno(paths.ToolsDir);
        }
    }

    public ProcessStartInfo YtDlp() => ToolProcess.StartInfo(this.YtDlpPath).With(this.JsRuntimeArgs);

    public ProcessStartInfo FFmpeg() => ToolProcess.StartInfo(this.FFmpegPath);

    public async Task<string> UpdateYtDlpAsync()
    {
        using var process = Process.Start(ToolProcess.StartInfo(this.YtDlpPath).With(["-U"]))
                            ?? throw new InvalidOperationException("Could not start yt-dlp.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = (await output).Trim();
        var error = (await errors).Trim();
        this.log.LogInformation("yt-dlp update: {Output}", text);
        // a failed update ends with exit code 100 (seen when GitHub timed out)
        if (process.ExitCode != 0)
        {
            this.log.LogWarning("yt-dlp update failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            throw new InvalidOperationException(error.Length > 0 ? error : text);
        }
        return text;
    }

    public async Task<ToolVersions> GetVersionsAsync() => new(
        await FirstLineAsync(this.YtDlpPath, "--version"),
        await FirstLineAsync(this.FFmpegPath, "-version"),
        await FirstLineAsync(this.DenoPath, "--version"));

    /// <summary>
    /// Shortens a version line to the version itself:
    /// <c>ffmpeg version 7.1-full_build …</c> → <c>7.1-full_build</c>, <c>deno 2.1.4 (stable …)</c> → <c>2.1.4</c>.
    /// </summary>
    public static string ShortVersion(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts switch
        {
            ["ffmpeg", "version", var version, ..] => version,
            ["deno", var version, ..] => version,
            _ => line.Trim(),
        };
    }

    private static async Task<string?> FirstLineAsync(string exe, string argument)
    {
        if (!File.Exists(exe))
            return null;
        var line = await ToolProcess.FirstLineAsync(ToolProcess.StartInfo(exe).With([argument]));
        return line is null ? null : ShortVersion(line);
    }

    /// <summary>Arguments that make yt-dlp use the bundled Deno runtime.</summary>
    private IReadOnlyList<string> JsRuntimeArgs =>
        File.Exists(this.DenoPath) ? ["--js-runtimes", $"deno:{this.DenoPath}"] : [];
}
