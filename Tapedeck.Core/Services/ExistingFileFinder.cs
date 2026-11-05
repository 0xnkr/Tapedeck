using System.Text.RegularExpressions;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>An audio file in the library folder and the video it belongs to, if known.</summary>
public sealed record LibraryFile(string FileName, string? VideoId);

/// <summary>
/// Finds the files of tracks that have none yet, e.g. after a CSV import into a library folder copied from another
/// computer, so they are taken over instead of downloaded twice. A file belongs to a video by the YouTube link that
/// <see cref="TagWriter"/> stores in the comment tag, or by the <c>[id]</c> in its name; files without either match by
/// the plain name <c>Artist - Title.opus</c>.
/// </summary>
public sealed partial class ExistingFileFinder(ITrackRepository repository, AppPaths paths)
{
    private static readonly string EXTENSION = $".{DownloadService.FORMAT}";

    [GeneratedRegex(@"\[([A-Za-z0-9_-]{11})\]$")]
    private static partial Regex IdInName();

    /// <summary>Gives the tracks without a file the matching file in the library folder.</summary>
    /// <param name="trackIds">Tracks to look at; <c>null</c> for all.</param>
    /// <returns>The tracks that got a file.</returns>
    public async Task<IReadOnlyList<long>> AdoptAsync(IReadOnlyCollection<long>? trackIds = null, CancellationToken ct = default)
    {
        var tracks = await repository.ListDisplayAsync();
        var candidates = tracks.Where(t => (trackIds is null || trackIds.Contains(t.Id)) && this.NeedsFile(t)).ToList();
        if (candidates.Count == 0)
            return [];

        // files of other tracks are never taken over, even when their tags say otherwise
        var claimed = tracks
            .Select(t => t.FilePath)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = await Task.Run(() => this.Scan(claimed, ct), ct);

        var matches = Match(candidates, files);
        foreach (var (trackId, fileName) in matches)
            await repository.UpdateDownloadStateAsync(trackId, DownloadStatus.Done, fileName, DownloadService.FORMAT);
        return [.. matches.Keys];
    }

    /// <summary>Pairs tracks with files: the video ID first, the plain file name only for files without one.</summary>
    public static IReadOnlyDictionary<long, string> Match(IEnumerable<TrackDisplay> tracks, IReadOnlyList<LibraryFile> files)
    {
        var byVideo = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.Where(f => f.VideoId is not null))
            byVideo.TryAdd(file.VideoId!, file.FileName);
        var byName = files
            .Where(f => f.VideoId is null)
            .Select(f => f.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matches = new Dictionary<long, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            var plainName = FileNaming.BaseName(track.Artist, track.Title) + EXTENSION;
            var file = byVideo.GetValueOrDefault(track.VideoId)
                       ?? (byName.TryGetValue(plainName, out var named) ? named : null);
            if (file is not null && used.Add(file))
                matches[track.Id] = file;
        }
        return matches;
    }

    /// <summary>The video of a file name like <c>Artist - Title [id].opus</c>, or <c>null</c>.</summary>
    public static string? VideoIdInName(string fileName)
    {
        var match = IdInName().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? match.Groups[1].Value : null;
    }

    // running downloads get their file anyway; missing files may have been moved back or renamed
    private bool NeedsFile(TrackDisplay track) =>
        track.DownloadStatus != DownloadStatus.Downloading
        && (track.FilePath is null || !File.Exists(paths.ToLibraryPath(track.FilePath)));

    private List<LibraryFile> Scan(HashSet<string> claimed, CancellationToken ct)
    {
        var files = new List<LibraryFile>();
        if (!Directory.Exists(paths.LibraryDir))
            return files;

        foreach (var path in Directory.EnumerateFiles(paths.LibraryDir, "*" + EXTENSION))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (!claimed.Contains(fileName))
                files.Add(new LibraryFile(fileName, ReadVideoId(path) ?? VideoIdInName(fileName)));
        }
        return files;
    }

    private static string? ReadVideoId(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return VideoId.TryParse(file.Tag.Comment, out var videoId) ? videoId : null;
        }
        catch (Exception)
        {
            // TagLib throws all kinds of exceptions for broken or foreign files; such a file just has no known video
            return null;
        }
    }
}
