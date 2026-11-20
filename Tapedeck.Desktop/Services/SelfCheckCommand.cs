using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Apple;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;

namespace Tapedeck.Services;

/// <summary>
/// <c>--self-check</c>: checks without a window what a platform needs, in a temporary folder, and exits with 1 on the
/// first failure. Made for CI on macOS, where nobody can try the app by hand: the tools download and start, OpenAL
/// loads, ffmpeg measures loudness, and the Apple export encodes and tags an <c>.m4a</c>.
/// </summary>
public static class SelfCheckCommand
{
    public static async Task RunAsync(ILoggerFactory? logging)
    {
        var log = logging?.CreateLogger("SelfCheck");
        var root = Path.Combine(Path.GetTempPath(), $"tapedeck-self-check-{Guid.NewGuid():N}");
        var paths = new AppPaths(Path.Combine(root, "data"), Path.Combine(root, "library"), Path.Combine(root, "apple"));
        paths.EnsureDirectories();
        try
        {
            Report(log, $"Data in {paths.DataDir}");

            var tools = new ToolService(paths, logging?.CreateLogger<ToolService>());
            await tools.SetupAsync();
            var versions = await tools.GetVersionsAsync();
            Report(log, $"yt-dlp {versions.YtDlp ?? "missing"}");
            Report(log, $"ffmpeg {versions.FFmpeg ?? "missing"}");
            Report(log, $"{tools.JsRuntimeName} {versions.JsRuntime ?? "missing"}");
            if (versions.YtDlp is null || versions.FFmpeg is null || versions.JsRuntime is null)
                throw new InvalidOperationException("A tool does not start.");

            if (!OpenAlPlayerService.CanLoadOpenAl())
                throw new InvalidOperationException("OpenAL Soft does not load.");
            Report(log, "OpenAL Soft loads");

            var tone = Path.Combine(paths.LibraryDir, "Tapedeck - Self Check.opus");
            await MakeToneAsync(tools, tone);
            var lufs = await new LoudnessMeter(tools).MeasureAsync(tone);
            Report(log, $"Loudness of the test tone: {lufs:0.0} LUFS");

            var copy = Path.Combine(paths.AppleExportDir, "Tapedeck - Self Check.m4a");
            Directory.CreateDirectory(paths.AppleExportDir);
            await new AacEncoder(tools).EncodeAsync(tone, copy);
            var track = new TrackDisplay { Artist = "Tapedeck", Title = "Self Check", Url = "https://example.org/self-check" };
            Mp4TagWriter.Write(copy, new TrackTagData(track, lufs, "[00:01.00]Line", true, null, null, null));
            using (var file = TagLib.File.Create(copy))
            {
                if (file.Tag.Title != "Self Check" || file.Tag.Lyrics != "Line" || file.Properties.AudioBitrate <= 0)
                    throw new InvalidOperationException("The .m4a copy lacks its tags.");
                Report(log, $"Apple copy: {file.Properties.Description}, {file.Properties.AudioBitrate} kbit/s, {new FileInfo(copy).Length} bytes");
            }

            Report(log, "Self-check passed");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // a temp folder left behind does no harm
            }
        }
    }

    // a two second sine as Opus, like a downloaded track
    private static async Task MakeToneAsync(IToolHost tools, string target)
    {
        foreach (var encoder in new[] { new[] { "-c:a", "libopus" }, new[] { "-c:a", "opus", "-strict", "-2", "-ac", "2" } })
        {
            var psi = tools.FFmpeg().With(["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=2"]);
            psi.With(encoder).With(["-b:a", "96k", target]);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg does not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await output;
            await errors;
            if (process.ExitCode == 0 && File.Exists(target))
                return;
        }
        throw new InvalidOperationException("ffmpeg could not make an Opus test tone.");
    }

    // stdout for the CI log, the log file for everything else
    private static void Report(ILogger? log, string line)
    {
        Console.WriteLine(line);
        log?.LogInformation("{Line}", line);
    }
}
