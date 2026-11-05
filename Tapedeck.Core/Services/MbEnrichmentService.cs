using System.Collections.Concurrent;
using System.Globalization;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Services;

/// <summary>
/// Matches a track with MusicBrainz: searches, decides with <see cref="MbMatcher"/> and stores the outcome.
/// A certain match is applied with its genres and cover, a plausible one is kept as a suggestion.
/// </summary>
public sealed class MbEnrichmentService(ITrackRepository repository, MbClient client, CoverArtService covers)
{
    // tracks of one album or artist share their genres; ask MusicBrainz once per app run
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> genres = new();

    /// <param name="keepMatch">
    /// A track matched before keeps its match when the search finds nothing certain, e.g. one the user confirmed;
    /// its genres and cover are fetched again.
    /// </param>
    /// <returns>The state the track is in afterwards.</returns>
    public async Task<MbMatchState> EnrichAsync(long trackId, bool keepMatch = false, CancellationToken ct = default)
    {
        var track = await repository.GetAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        var query = MbQuery.From(track);
        if (query is null)
        {
            await repository.SaveMbResultAsync(trackId, MbMatchState.NoMatch, null, [], null, MbCandidatesJson.Serialize([]));
            return MbMatchState.NoMatch;
        }

        var result = MbMatchResult.NONE;
        if (query.StrictLucene is { } strict)
            result = MbMatcher.Match(query, await client.SearchRecordingsAsync(strict, MbClient.STRICT_LIMIT, ct), fallback: false);

        // wrong artist or title from YouTube: search the words of the raw title instead
        if (result.Decision == MbDecision.NoMatch && query.FallbackLucene is { } fallbackQuery)
        {
            var fallback = MbMatcher.Match(query, await client.SearchRecordingsAsync(fallbackQuery, MbClient.FALLBACK_LIMIT, ct), fallback: true);
            if ((fallback.Best?.Score ?? 0) > (result.Best?.Score ?? 0))
                result = fallback;
        }

        var candidates = MbCandidatesJson.Serialize(result.Candidates.Select(c => c.Pick));
        if (keepMatch && result.Decision != MbDecision.Matched && StoredPick(track) is { } stored)
        {
            await this.ApplyAsync(trackId, stored, ct: ct);
            return MbMatchState.Matched;
        }

        switch (result.Decision)
        {
            case MbDecision.Matched:
                await this.ApplyAsync(trackId, result.Best!.Pick, candidates, ct);
                return MbMatchState.Matched;
            case MbDecision.Suggested:
                await repository.SaveMbResultAsync(trackId, MbMatchState.Suggested, null, [], result.Best!.Score, candidates);
                return MbMatchState.Suggested;
            default:
                await repository.SaveMbResultAsync(trackId, MbMatchState.NoMatch, null, [], result.Best?.Score, candidates);
                return MbMatchState.NoMatch;
        }
    }

    /// <summary>Takes over a found song: values, genres and the release's cover.</summary>
    /// <param name="candidatesJson"><c>null</c> keeps the stored candidates, e.g. when the user confirms one of them.</param>
    public async Task ApplyAsync(long trackId, MbPick pick, string? candidatesJson = null, CancellationToken ct = default)
    {
        var genreList = await this.GenresAsync(pick, ct);
        await repository.SaveMbResultAsync(trackId, MbMatchState.Matched, pick, genreList, pick.Score, candidatesJson);

        string? cover;
        try
        {
            cover = await covers.DownloadAsync(pick.ReleaseId, pick.ReleaseGroupId, ct);
        }
        catch (HttpRequestException)
        {
            // the match stays without cover; the thumbnail keeps serving
            cover = null;
        }

        var track = await repository.GetAsync(trackId);
        if (cover is not null && track?.CoverSource is null or "thumb")
            await repository.SetCoverAsync(trackId, cover, "caa");
    }

    /// <summary>Removes a match or a suggestion; bulk runs leave the track alone afterwards.</summary>
    public Task RejectAsync(long trackId) =>
        repository.SaveMbResultAsync(trackId, MbMatchState.Rejected, null, [], null, null);

    /// <summary>The match applied to the track, as stored in its <c>mb_*</c> columns; <c>null</c> without one.</summary>
    private static MbPick? StoredPick(Track track) =>
        track.MbState == MbMatchState.Matched && track.MbRecordingId is { } recordingId && track.MbReleaseId is { } releaseId
            ? new MbPick(
                recordingId,
                track.MbTitle ?? string.Empty,
                track.MbArtist ?? string.Empty,
                track.MbArtistId ?? string.Empty,
                releaseId,
                track.MbReleaseGroupId ?? string.Empty,
                track.MbAlbum ?? string.Empty,
                track.MbAlbumArtist ?? track.MbArtist ?? string.Empty,
                track.MbReleaseDate,
                track.MbTrackNo,
                track.MbDiscNo,
                null,
                track.MbScore ?? 1)
            : null;

    /// <summary>
    /// Genres of the album, else of the recording, else of the artist. Singles rarely have votes of their own,
    /// the artist almost always does.
    /// </summary>
    private async Task<IReadOnlyList<string>> GenresAsync(MbPick pick, CancellationToken ct)
    {
        foreach (var (entity, id) in new[] { ("release-group", pick.ReleaseGroupId), ("recording", pick.RecordingId), ("artist", pick.ArtistId) })
        {
            var found = await this.GenresOfAsync(entity, id, ct);
            if (found.Count > 0)
                return found;
        }
        return [];
    }

    private async Task<IReadOnlyList<string>> GenresOfAsync(string entity, string id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id))
            return [];
        var key = $"{entity}/{id}";
        if (this.genres.TryGetValue(key, out var cached))
            return cached;

        IReadOnlyList<string> found;
        try
        {
            // MusicBrainz writes genres in lower case: "west coast hip hop" -> "West Coast Hip Hop"
            found = (await client.GetGenresAsync(entity, id, ct))
                .Select(g => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(g))
                .ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }

        this.genres[key] = found;
        return found;
    }
}
