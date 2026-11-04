using Tapedeck.Core.Models;
using YoutubeDLSharp.Options;

namespace Tapedeck.Core.Services;

/// <summary>
/// Downloads a track as Opus into the flat library: first into the temp folder under its video ID,
/// then renamed to <c>Artist - Title.opus</c> and tagged.
/// </summary>
public sealed class DownloadService(ITrackRepository repository, ToolService tools, TagWriter tagWriter, AppPaths paths)
{
    public const string FORMAT = "opus";

    public async Task DownloadAsync(long trackId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var track = await repository.GetDisplayAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");

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

            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Downloading, fileName, FORMAT);
            await tagWriter.WriteTagsAsync(trackId);
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Done);
        }
        catch (OperationCanceledException)
        {
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Pending);
            this.CleanTemp(track.VideoId);
            throw;
        }
        catch (Exception ex)
        {
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Error, errorMsg: ex.Message);
            this.CleanTemp(track.VideoId);
            throw;
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
