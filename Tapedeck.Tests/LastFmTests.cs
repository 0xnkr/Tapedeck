using Dapper;
using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Genres from Last.fm for tracks MusicBrainz has none for.</summary>
public sealed class LastFmTests : IAsyncLifetime
{
    // built after the documented answer of artist.getTopTags; a real one needs an API key
    private const string TOP_TAGS = """
        {"toptags": {"tag": [
          {"count": 100, "name": "hip-hop", "url": "https://www.last.fm/tag/hip-hop"},
          {"count": 61, "name": "rap", "url": "https://www.last.fm/tag/rap"},
          {"count": 40, "name": "seen live", "url": "https://www.last.fm/tag/seen+live"},
          {"count": 22, "name": "west coast", "url": "https://www.last.fm/tag/west+coast"},
          {"count": 18, "name": "gangsta rap", "url": "https://www.last.fm/tag/gangsta+rap"},
          {"count": 12, "name": "g-funk", "url": "https://www.last.fm/tag/g-funk"},
          {"count": 4, "name": "funk", "url": "https://www.last.fm/tag/funk"}
        ], "@attr": {"artist": "Coolio"}}}
        """;

    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-lastfm-{Guid.NewGuid():N}");
    private SqliteConnectionFactory factory = null!;
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        this.factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(this.factory).MigrateAsync();
        this.repo = new TrackRepository(this.factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> AddAsync(string videoId, string artist, string title)
    {
        var id = await this.repo.AddAsync(videoId, VideoId.WatchUrl(videoId));
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata(videoId, $"{artist} - {title}", artist, "20200101", 200,
            null, null, null, null, artist, title, ["Music"], "{}"));
        return id;
    }

    [Fact]
    public void Top_tags_are_read_strongest_first()
    {
        var tags = LastFmJson.ParseTopTags(TOP_TAGS);

        Assert.Equal(7, tags.Count);
        Assert.Equal(new LastFmTag("hip-hop", 100), tags[0]);
        Assert.Equal(new LastFmTag("funk", 4), tags[^1]);
    }

    [Fact]
    public void An_error_answer_is_an_exception_with_its_code()
    {
        var error = Assert.Throws<LastFmException>(() =>
            LastFmJson.ParseTopTags("""{"error": 10, "message": "Invalid API key - You must be granted a valid key by last.fm"}"""));

        Assert.Equal(LastFmException.INVALID_KEY, error.Code);
    }

    [Fact]
    public void A_single_tag_comes_as_object() =>
        Assert.Equal([new LastFmTag("k-pop", 100)], LastFmJson.ParseTopTags("""{"toptags": {"tag": {"count": "100", "name": "k-pop"}}}"""));

    [Fact]
    public void Only_strong_tags_that_map_to_a_main_genre_are_kept() =>
        // "seen live" and "west coast" map to nothing, "funk" is too weak
        Assert.Equal(["Hip-Hop", "Rap", "Gangsta Rap"], LastFmGenreService.Pick(LastFmJson.ParseTopTags(TOP_TAGS)));

    [Fact]
    public async Task Tracks_without_genres_get_the_artist_tags_once_per_artist()
    {
        var first = await this.AddAsync("aaaaaaaaaaa", "Coolio", "Gangsta's Paradise");
        var second = await this.AddAsync("bbbbbbbbbbb", "Coolio", "Fantastic Voyage");
        var matched = await this.AddAsync("ccccccccccc", "Kendrick Lamar", "Alright");
        await this.repo.SetUserTagsAsync(matched, ["Conscious Hip Hop"]);
        var lastFm = new FakeLastFm { ["Coolio"] = LastFmJson.ParseTopTags(TOP_TAGS) };
        var service = new LastFmGenreService(this.repo, lastFm);

        var summary = await service.FetchAsync("key");

        Assert.Equal(["Coolio"], lastFm.Asked);
        Assert.Equal((2, 2), (summary.Asked, summary.WithGenres));
        Assert.Equal([first, second], summary.Tagged.Order());
        var track = (await this.repo.GetDisplayAsync(first))!;
        Assert.Equal(("Gangsta Rap;Hip-Hop;Rap", FieldSource.Lastfm), (track.Genres, track.GenresFrom));
        Assert.Equal(["Hip-Hop"], track.MainGenreList);
        // the user's genres win and the track was never asked for
        Assert.Equal(FieldSource.User, (await this.repo.GetDisplayAsync(matched))!.GenresFrom);
        Assert.Equal(0, await service.CountPendingAsync());
    }

    [Fact]
    public async Task An_artist_without_tags_is_not_asked_again()
    {
        await this.AddAsync("aaaaaaaaaaa", "Unknown Garage Band", "Demo");
        var lastFm = new FakeLastFm();
        var service = new LastFmGenreService(this.repo, lastFm);

        var summary = await service.FetchAsync("key");

        Assert.Equal((1, 0), (summary.Asked, summary.WithGenres));
        Assert.Equal(0, await service.CountPendingAsync());
    }

    [Fact]
    public async Task A_collaboration_falls_back_to_the_main_artist()
    {
        await this.AddAsync("aaaaaaaaaaa", "Coolio feat. L.V.", "Gangsta's Paradise");
        var lastFm = new FakeLastFm { ["Coolio"] = LastFmJson.ParseTopTags(TOP_TAGS) };

        var summary = await new LastFmGenreService(this.repo, lastFm).FetchAsync("key");

        Assert.Equal(["Coolio feat. L.V.", "Coolio"], lastFm.Asked);
        Assert.Equal(1, summary.WithGenres);
    }

    [Fact]
    public async Task An_invalid_key_stops_and_leaves_the_tracks_waiting()
    {
        await this.AddAsync("aaaaaaaaaaa", "Coolio", "Gangsta's Paradise");
        var lastFm = new FakeLastFm { Error = new LastFmException(LastFmException.INVALID_KEY, "Invalid API key") };
        var service = new LastFmGenreService(this.repo, lastFm);

        var summary = await service.FetchAsync("wrong");

        Assert.True(summary.InvalidKey);
        Assert.Equal(1, await service.CountPendingAsync());
    }

    [Fact]
    public async Task Migration_marks_tagged_files_with_genres_outdated()
    {
        // the state before migration 010: files tagged with their fine genres
        var tagged = await this.AddAsync("aaaaaaaaaaa", "Coolio", "Gangsta's Paradise");
        var untagged = await this.AddAsync("bbbbbbbbbbb", "Coolio", "Fantastic Voyage");
        await this.repo.UpdateDownloadStateAsync(tagged, DownloadStatus.Done, "a.opus", "opus");
        await this.repo.UpdateDownloadStateAsync(untagged, DownloadStatus.Done, "b.opus", "opus");
        await this.repo.SetUserTagsAsync(tagged, ["G-Funk"]);
        await this.repo.MarkTagsWrittenAsync(tagged);
        await this.repo.MarkTagsWrittenAsync(untagged);
        await Task.Delay(5);
        Assert.False((await this.repo.GetDisplayAsync(tagged))!.TagsStale);

        await using (var connection = await this.factory.OpenAsync())
        {
            var sql = File.ReadAllText(Path.Combine(SourceDir(), "Tapedeck.Data", "Migrations", "010_genres.sql"));
            // the column exists already, only the update is under test
            await connection.ExecuteAsync(sql[..sql.IndexOf("ALTER TABLE", StringComparison.Ordinal)]);
        }

        Assert.True((await this.repo.GetDisplayAsync(tagged))!.TagsStale);
        Assert.False((await this.repo.GetDisplayAsync(untagged))!.TagsStale);
    }

    private static string SourceDir([System.Runtime.CompilerServices.CallerFilePath] string testFile = "") =>
        Path.Combine(Path.GetDirectoryName(testFile)!, "..");

    /// <summary>Answers from a dictionary by artist; unknown artists have no tags.</summary>
    private sealed class FakeLastFm : Dictionary<string, IReadOnlyList<LastFmTag>>, ILastFmTags
    {
        public List<string> Asked { get; } = [];
        public LastFmException? Error { get; init; }

        public Task<IReadOnlyList<LastFmTag>> GetArtistTagsAsync(string apiKey, string artist, CancellationToken ct = default)
        {
            this.Asked.Add(artist);
            if (this.Error is { } error)
                throw error;
            return Task.FromResult(this.TryGetValue(artist, out var tags) ? tags : (IReadOnlyList<LastFmTag>)[]);
        }
    }
}
