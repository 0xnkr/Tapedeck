using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.Models;
using Tapedeck.Core.Recommendations;
using Tapedeck.Core.Spotify;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>TAPEDECK_TAGS and TAPEDECK_HISTORY for the phone app's recommendations.</summary>
public sealed class RecommendationTests : IAsyncLifetime
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-reco-{Guid.NewGuid():N}");
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        var factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(factory).MigrateAsync();
        this.repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> AddAsync(string videoId, string artist, string title, bool tagged = false)
    {
        var id = await this.repo.AddAsync(videoId, VideoId.WatchUrl(videoId));
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata(videoId, $"{artist} - {title}", artist, "20200101", 200,
            null, null, null, null, artist, title, ["Music"], "{}"));
        if (tagged)
        {
            await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, $"{videoId}.opus", "opus");
            await this.repo.MarkTagsWrittenAsync(id);
            await Task.Delay(5);
        }
        return id;
    }

    private static SpotifyStream Stream(string artist, string title, string at, int seconds, string? end = "trackdone", bool incognito = false) =>
        new(DateTimeOffset.Parse(at), $"spotify:track:{artist}-{title}", artist, title, null, seconds * 1000, "clickrow", end, incognito);

    // --- tag selection and format ---

    [Fact]
    public void Selection_keeps_strong_tags_without_noise_lowercase_and_strongest_first()
    {
        var tags = RecommendationTags.Select([
            new LastFmTag("Synthwave", 100),
            new LastFmTag("seen live", 90),
            new LastFmTag("Chill", 40),
            new LastFmTag("chill", 12),
            new LastFmTag("Female  Vocalists", 31),
            new LastFmTag("albums I own", 30),
            new LastFmTag("dreamy", 9),
        ]);

        Assert.Equal([new ArtistTag("synthwave", 100), new ArtistTag("chill", 40), new ArtistTag("female vocalists", 31)], tags);
    }

    [Fact]
    public void Selection_keeps_at_most_fifteen_tags()
    {
        var tags = RecommendationTags.Select(Enumerable.Range(0, 20).Select(i => new LastFmTag($"tag {i:00}", 100 - i)).ToList());

        Assert.Equal(RecommendationTags.MAX_TAGS, tags.Count);
        Assert.Equal("tag 14", tags[^1].Tag);
    }

    [Theory]
    [InlineData("  Hip-Hop ", "hip-hop")]
    [InlineData("rock;pop", "rock pop")]
    [InlineData("a=b", "a b")]
    [InlineData("80s\nsynth", "80s synth")]
    [InlineData(" ; ", null)]
    public void Tag_names_are_normalized(string name, string? expected) =>
        Assert.Equal(expected, RecommendationTags.NormalizeTag(name));

    [Fact]
    public void Tags_are_formatted_with_two_decimals()
    {
        var text = RecommendationTags.FormatTags([new ArtistTag("chill", 40), new ArtistTag("synthwave", 82), new ArtistTag("female vocalists", 31)]);

        Assert.Equal("synthwave=0.82;chill=0.40;female vocalists=0.31", text);
    }

    [Fact]
    public void No_tags_remove_the_field() =>
        Assert.Null(RecommendationTags.FormatTags([]));

    [Fact]
    public void History_is_formatted_with_plays_skips_and_day()
    {
        Assert.Equal("plays=57;skips=3;last=2025-03-14", RecommendationTags.FormatHistory(new ListeningHistory(57, 3, new DateOnly(2025, 3, 14))));
        Assert.Equal("plays=0;skips=2", RecommendationTags.FormatHistory(new ListeningHistory(0, 2, null)));
        Assert.Null(RecommendationTags.FormatHistory(new ListeningHistory(0, 0, new DateOnly(2025, 3, 14))));
        Assert.Null(RecommendationTags.FormatHistory(null));
    }

    // --- Last.fm artist tags ---

    [Fact]
    public async Task Artist_tags_are_stored_for_tracks_with_genres_too_and_mark_their_files()
    {
        var withGenres = await this.AddAsync("aaaaaaaaaaa", "Neon Harbor", "Night Drive", tagged: true);
        await this.repo.SetUserTagsAsync(withGenres, ["Synthwave"]);
        await this.repo.MarkTagsWrittenAsync(withGenres);
        await Task.Delay(5);
        var lastFm = new FakeLastFm
        {
            ["Neon Harbor"] = [new LastFmTag("synthwave", 100), new LastFmTag("seen live", 50), new LastFmTag("retro", 20)],
        };
        var service = new LastFmGenreService(this.repo, lastFm);
        Assert.Equal(1, await service.CountPendingAsync());

        var summary = await service.FetchAsync("key");

        Assert.Equal(["Neon Harbor"], lastFm.Asked);
        Assert.Equal((0, 1), (summary.Asked, summary.ArtistsAsked));
        Assert.Equal([withGenres], summary.Tagged);
        Assert.Equal([new ArtistTag("synthwave", 100), new ArtistTag("retro", 20)],
            await this.repo.GetArtistTagsAsync(RecommendationTags.ArtistKey("Neon Harbor")));
        Assert.True((await this.repo.GetDisplayAsync(withGenres))!.TagsStale);
        Assert.Equal(0, await service.CountPendingAsync());
    }

    [Fact]
    public async Task Unchanged_artist_tags_leave_the_files_as_they_are()
    {
        var id = await this.AddAsync("aaaaaaaaaaa", "Neon Harbor", "Night Drive", tagged: true);
        var key = RecommendationTags.ArtistKey("Neon Harbor");
        Assert.True(await this.repo.SaveArtistTagsAsync(key, [new ArtistTag("synthwave", 100)], [id]));
        await this.repo.MarkTagsWrittenAsync(id);
        await Task.Delay(5);

        Assert.False(await this.repo.SaveArtistTagsAsync(key, [new ArtistTag("synthwave", 100)], [id]));

        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);
    }

    [Fact]
    public async Task An_artist_without_tags_is_remembered()
    {
        var id = await this.AddAsync("aaaaaaaaaaa", "Garage Demo Band", "Tape One");
        await this.repo.SetUserTagsAsync(id, ["Rock"]);
        var service = new LastFmGenreService(this.repo, new FakeLastFm());

        await service.FetchAsync("key");

        Assert.Contains(RecommendationTags.ArtistKey("Garage Demo Band"), await this.repo.ListCheckedArtistKeysAsync());
        Assert.Equal(0, await service.CountPendingAsync());
    }

    // --- Spotify history ---

    [Fact]
    public void Plays_and_early_skips_are_counted_per_track()
    {
        var library = new Dictionary<string, long>
        {
            [SpotifyAnalyzer.SongKey("Neon Harbor", "Night Drive")] = 1,
            [SpotifyAnalyzer.SongKey("Paper Lanterns", "Slow Tide")] = 2,
        };
        var streams = new[]
        {
            Stream("Neon Harbor", "Night Drive", "2024-01-05T20:00:00Z", 210),
            // the remaster is the same song
            Stream("Neon Harbor", "Night Drive - 2020 Remaster", "2025-03-14T23:30:00Z", 205),
            // too short for a play, too long for an early skip
            Stream("Neon Harbor", "Night Drive", "2025-01-01T12:00:00Z", 20, "fwdbtn"),
            Stream("Paper Lanterns", "Slow Tide", "2024-06-01T09:00:00Z", 4, "fwdbtn"),
            Stream("Paper Lanterns", "Slow Tide", "2024-06-02T09:00:00Z", 9, "fwdbtn"),
            // ended by the app, not by the user
            Stream("Paper Lanterns", "Slow Tide", "2024-06-03T09:00:00Z", 3, "endplay"),
            Stream("Not In Library", "Elsewhere", "2024-06-03T09:00:00Z", 200),
        };

        var byTrack = SpotifyTaste.ByTrack(streams, library);

        Assert.Equal(2, byTrack.Count);
        Assert.Equal(new ListeningHistory(2, 0, new DateOnly(2025, 3, 14)), byTrack[1]);
        Assert.Equal(new ListeningHistory(0, 2, new DateOnly(2024, 6, 2)), byTrack[2]);
    }

    [Fact]
    public async Task Taking_over_a_history_replaces_the_old_one_and_marks_changed_files()
    {
        var kept = await this.AddAsync("aaaaaaaaaaa", "Neon Harbor", "Night Drive", tagged: true);
        var dropped = await this.AddAsync("bbbbbbbbbbb", "Paper Lanterns", "Slow Tide", tagged: true);
        await this.repo.SetListeningHistoryAsync(new Dictionary<long, ListeningHistory>
        {
            [kept] = new(1, 0, new DateOnly(2024, 1, 5)),
            [dropped] = new(3, 0, new DateOnly(2024, 1, 5)),
        });
        await this.repo.MarkTagsWrittenAsync(kept);
        await this.repo.MarkTagsWrittenAsync(dropped);
        await Task.Delay(5);
        var history = new SpotifyHistory(
            [Stream("Neon Harbor", "Night Drive", "2024-01-05T20:00:00Z", 210)],
            new SpotifyReadStats(1, 1, 0, 0, 0));

        var result = await new SpotifyTasteService(this.repo, new SpotifyMatchRepository(this.Factory())).ApplyAsync(history);

        Assert.Equal(1, result.Tracks);
        Assert.Equal([dropped], result.Changed);
        Assert.Equal(new ListeningHistory(1, 0, new DateOnly(2024, 1, 5)), await this.repo.GetListeningHistoryAsync(kept));
        Assert.Null(await this.repo.GetListeningHistoryAsync(dropped));
        Assert.False((await this.repo.GetDisplayAsync(kept))!.TagsStale);
        Assert.True((await this.repo.GetDisplayAsync(dropped))!.TagsStale);
    }

    private SqliteConnectionFactory Factory() => new(Path.Combine(this.dir, "tapedeck.db"));

    /// <summary>Answers from a dictionary by artist; unknown artists have no tags.</summary>
    private sealed class FakeLastFm : Dictionary<string, IReadOnlyList<LastFmTag>>, ILastFmTags
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<LastFmTag>> GetArtistTagsAsync(string apiKey, string artist, CancellationToken ct = default)
        {
            this.Asked.Add(artist);
            return Task.FromResult(this.TryGetValue(artist, out var tags) ? tags : (IReadOnlyList<LastFmTag>)[]);
        }
    }
}
