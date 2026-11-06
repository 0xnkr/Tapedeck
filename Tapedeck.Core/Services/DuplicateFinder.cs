using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Services;

/// <summary>
/// Finds tracks that are the same song from different videos, e.g. the music video, the "Topic" upload and a
/// lyric video. Tracks matched with MusicBrainz are the same song when they are the same recording; two matched
/// tracks with different recordings never are, even with the same name (a remaster, another song called "Intro").
/// A track without a match joins the group with the same artist and title.
/// </summary>
public static class DuplicateFinder
{
    /// <summary>What the search needs of a track: the shown artist and title and the MusicBrainz recording.</summary>
    public sealed record Song(long Id, string Artist, string Title, string? MbRecordingId);

    /// <summary>Groups of at least two tracks, each ordered by id, the groups by their first track.</summary>
    public static IReadOnlyList<IReadOnlyList<long>> Groups(IEnumerable<TrackDisplay> tracks) =>
        Groups(tracks.Select(t => new Song(t.Id, t.Artist, t.Title, t.MbRecordingId)));

    /// <inheritdoc cref="Groups(IEnumerable{TrackDisplay})"/>
    public static IReadOnlyList<IReadOnlyList<long>> Groups(IEnumerable<Song> tracks)
    {
        var list = tracks.OrderBy(t => t.Id).ToList();
        var byRecording = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        // null marks a name shared by different recordings: an unmatched track cannot tell which one it is
        var byName = new Dictionary<string, List<long>?>(StringComparer.Ordinal);

        foreach (var track in list.Where(t => !string.IsNullOrEmpty(t.MbRecordingId)))
        {
            if (!byRecording.TryGetValue(track.MbRecordingId!, out var group))
                byRecording[track.MbRecordingId!] = group = [];
            group.Add(track.Id);

            var name = NameKey(track);
            if (!byName.TryGetValue(name, out var named))
                byName[name] = group;
            else if (named != group)
                byName[name] = null;
        }

        var unmatched = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        foreach (var track in list.Where(t => string.IsNullOrEmpty(t.MbRecordingId)))
        {
            var name = NameKey(track);
            if (byName.TryGetValue(name, out var group) && group is not null)
                group.Add(track.Id);
            else if (unmatched.TryGetValue(name, out var others))
                others.Add(track.Id);
            else
                unmatched[name] = [track.Id];
        }

        return byRecording.Values.Concat(unmatched.Values)
            .Where(g => g.Count > 1)
            .Select(g => (IReadOnlyList<long>)g.Order().ToList())
            .OrderBy(g => g[0])
            .ToList();
    }

    /// <summary>Artist and title as shown, without case, accents and punctuation.</summary>
    private static string NameKey(Song track) =>
        MbNormalizer.Normalize(track.Artist) + "\n" + MbNormalizer.Normalize(track.Title);
}
