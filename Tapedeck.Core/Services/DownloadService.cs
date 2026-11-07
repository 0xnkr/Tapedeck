using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Models;
using YoutubeDLSharp.Options;

namespace Tapedeck.Core.Services;

/// <summary>
/// Downloads a track as Opus into the flat library: first into the temp folder under its video ID,
/// then renamed to <c>Artist - Title.opus</c> and tagged.
/// </summary>
public sealed class DownloadService(
    ITrackRepository repository,
    ToolService tools,
    TagWriter tagWriter,
    LoudnessMeter loudness,
    AppPaths paths,
    ILogger<DownloadService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public const string FORMAT = "opus";

    public async Task DownloadAsync(long trackId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var track = await repository.GetDisplayAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");

        this.log.LogInformation("Downloading {VideoId} ({Artist} - {Title})", track.VideoId, track.Artist, track.Title);
        var watch = Stopwatch.StartNew();
        await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Downloading);
        Directory.CreateDirectory(paths.TempDir);
        Directory.CreateDirectory(paths.LibraryDir);

        try
        {
            var options = new OptionSet
            {
                Output = Path.Combine(paths.TempDir, $"{track.VideoId}.%(ext)s"),
                NoPlaylist = true,
            };
            if (tools.JsRuntimeArgs is [var flag, var value])
                options.AddCustomOption(flag, value);

            var ytProgress = progress is null
                ? null
                : new Progress<YoutubeDLSharp.DownloadProgress>(p => progress.Report(p.Progress));

            var result = await tools.CreateClient()
                .RunAudioDownload(track.Url, AudioConversionFormat.Opus, ct, ytProgress, overrideOptions: options);
            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.ErrorOutput));

            var fileName = FileNaming.ChooseForTrack(track.FilePath, track.Artist, track.Title, track.VideoId, FORMAT,
                name => File.Exists(paths.ToLibraryPath(name)));
            // overwrite only ever hits the track's own file from an earlier download
            File.Move(result.Data, paths.ToLibraryPath(fileName), overwrite: true);
            // a correction since the last download gave the file a new name; drop the outdated copy
            if (track.FilePath is { } oldFile && !string.Equals(oldFile, fileName, StringComparison.OrdinalIgnoreCase))
                File.Delete(paths.ToLibraryPath(oldFile));

            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Downloading, fileName, FORMAT);
            await this.MeasureLoudnessAsync(trackId, fileName, ct);
            await tagWriter.WriteTagsAsync(trackId);
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Done);
            this.log.LogInformation("Downloaded {VideoId} as {File} ({Megabytes:0.0} MB in {Seconds:0.0} s)", track.VideoId,
                fileName, new FileInfo(paths.ToLibraryPath(fileName)).Length / 1048576.0, watch.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // a cancelled repeat download leaves the earlier file in place
            var hadFile = track.FilePath is { } file && File.Exists(paths.ToLibraryPath(file));
            this.log.LogInformation("Download of {VideoId} cancelled", track.VideoId);
            await repository.UpdateDownloadStateAsync(trackId, hadFile ? DownloadStatus.Done : DownloadStatus.Pending);
            this.CleanTemp(track.VideoId);
            throw;
        }
        catch (Exception ex)
        {
            this.log.LogError(ex, "Download of {VideoId} failed", track.VideoId);
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Error, errorMsg: ex.Message);
            this.CleanTemp(track.VideoId);
            throw;
        }
    }

    /// <summary>
    /// Measures the new file, so its track gain goes into the tags right away. A failed measurement does not
    /// fail the download: the track then simply has no gain, like before.
    /// </summary>
    private async Task MeasureLoudnessAsync(long trackId, string fileName, CancellationToken ct)
    {
        try
        {
            await repository.SetLoudnessAsync(trackId, await loudness.MeasureAsync(paths.ToLibraryPath(fileName), ct));
        }
        catch (InvalidOperationException ex)
        {
            // the background measurement (LoudnessQueue in the app) tries it again later
            this.log.LogWarning("Could not measure the loudness of {File}: {Error}", fileName, ex.Message);
        }
    }

    private void CleanTemp(string videoId)
    {
        foreach (var file in Directory.EnumerateFiles(paths.TempDir, $"{videoId}.*"))
        {
            try { File.Delete(file); }
            catch (IOException) { /* file still locked, will be overwritten on the next run */ }
        }
    }
}
