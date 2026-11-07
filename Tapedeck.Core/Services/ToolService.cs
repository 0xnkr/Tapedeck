using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.Diagnostics;
using YoutubeDLSharp;

namespace Tapedeck.Core.Services;

/// <summary>
/// Manages yt-dlp, ffmpeg and Deno in the app's tools folder.
/// yt-dlp needs Deno as its JS runtime to fully extract YouTube.
/// </summary>
public sealed class ToolService(AppPaths paths, ILogger<ToolService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public string YtDlpPath => Path.Combine(paths.ToolsDir, Utils.YtDlpBinaryName);
    public string FFmpegPath => Path.Combine(paths.ToolsDir, Utils.FfmpegBinaryName);
    public string DenoPath => Path.Combine(paths.ToolsDir, OperatingSystem.IsWindows() ? "deno.exe" : "deno");

    /// <summary>All binaries are present, so <see cref="EnsureToolsAsync"/> has nothing to download.</summary>
    public bool HasAllTools => File.Exists(this.YtDlpPath) && File.Exists(this.FFmpegPath) && File.Exists(this.DenoPath);

    /// <summary>Downloads missing binaries. Existing ones are left untouched.</summary>
    public async Task EnsureToolsAsync()
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

    /// <summary><c>yt-dlp -U</c>. Without regular updates yt-dlp breaks whenever YouTube changes.</summary>
    public async Task<string> UpdateYtDlpAsync()
    {
        var output = await this.CreateClient().RunUpdate();
        this.log.LogInformation("yt-dlp update: {Output}", output.Trim());
        return output;
    }

    public YoutubeDL CreateClient() => new()
    {
        YoutubeDLPath = this.YtDlpPath,
        FFmpegPath = this.FFmpegPath,
        OutputFolder = paths.TempDir,
    };

    /// <summary>Installed versions; <c>null</c> for a tool that is missing or does not start.</summary>
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

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var line = await process.StandardOutput.ReadLineAsync();
            await process.WaitForExitAsync();
            return string.IsNullOrWhiteSpace(line) ? null : ShortVersion(line);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Arguments that make yt-dlp use the bundled Deno runtime.</summary>
    public IReadOnlyList<string> JsRuntimeArgs =>
        File.Exists(this.DenoPath) ? ["--js-runtimes", $"deno:{this.DenoPath}"] : [];
}

/// <summary>Versions of the bundled tools; <c>null</c> when a tool is missing.</summary>
public sealed record ToolVersions(string? YtDlp, string? FFmpeg, string? Deno);
