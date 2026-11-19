using System.ComponentModel;
using System.Diagnostics;

namespace Tapedeck.Core.Services;

/// <summary>
/// Cuts a square cover out of the YouTube thumbnail for tracks without a cover from the Cover Art Archive,
/// so phones show a square cover instead of a 16:9 picture. The middle of the picture is taken: "Topic" uploads
/// show the album cover there, and a 4:3 thumbnail with black bars (hqdefault) is cut inside the bars.
/// Uses the bundled ffmpeg, no image library.
/// </summary>
public sealed class ThumbnailCoverService(IToolHost tools, AppPaths paths)
{
    /// <summary>File names of cut covers start with this, next to the release covers from the Cover Art Archive.</summary>
    public const string FILE_PREFIX = "yt-";

    // the largest square inside the 16:9 picture, scaled down to the size of Cover Art Archive covers
    private const string FILTER = "crop=w='min(ih\\,iw*9/16)':h='min(ih\\,iw*9/16)',scale=w='min(500\\,iw)':h=-1";

    /// <summary>ffmpeg can run; without it no cover is cut.</summary>
    public bool IsAvailable => tools.HasFFmpeg;

    /// <summary>The cover file of a video, whether it exists or not.</summary>
    public string PathFor(string videoId) => Path.Combine(paths.CoversDir, FILE_PREFIX + videoId + ".jpg");

    /// <summary>Cuts the cover, or takes the one cut before.</summary>
    /// <returns>The cover file.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg is missing or could not read the thumbnail.</exception>
    public async Task<string> CutAsync(string thumbPath, string videoId, CancellationToken cancellation = default)
    {
        var target = this.PathFor(videoId);
        if (File.Exists(target))
            return target;
        if (!File.Exists(thumbPath))
            throw new InvalidOperationException($"The thumbnail '{thumbPath}' does not exist.");

        Directory.CreateDirectory(paths.CoversDir);
        // the extension tells ffmpeg the format; a cancelled run never leaves half a cover under the real name
        var temp = Path.Combine(paths.CoversDir, FILE_PREFIX + videoId + ".tmp.jpg");
        var psi = tools.FFmpeg();
        foreach (var argument in new[] { "-hide_banner", "-v", "error", "-nostdin", "-y", "-i", thumbPath, "-vf", FILTER, "-q:v", "3", temp })
            psi.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg could not be started.");
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
            var log = (await errors).Trim();
            if (process.ExitCode != 0 || !File.Exists(temp))
                throw new InvalidOperationException($"ffmpeg could not cut the cover (exit code {process.ExitCode}): {log}");

            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"ffmpeg could not be started: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
