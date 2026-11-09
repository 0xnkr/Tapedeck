using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Core.Spotify;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Finding Spotify songs on YouTube, remembering the searches and importing the chosen videos.</summary>
public sealed class SpotifyMatchTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-spotify-match-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;
    private SpotifyMatchRepository matches = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
        this.matches = new SpotifyMatchRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>A real search for "Coolio Gangsta's Paradise topic": five Topic uploads, two label videos, one fan upload.</summary>
    private static IReadOnlyList<YtSearchResult> Search() =>
        MetadataService.ParseSearch(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "YouTube", "search-gangstas-paradise.json")));

    private static SpotifySong Song(string artist, string title, int? durationMs = null, int realPlays = 10, int streams = 12) =>
        new(SpotifyAnalyzer.SongKey(artist, title), artist, title, null, [$"spotify:track:{title.Length}"], realPlays, streams, 0,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new Dictionary<int, int>(), durationMs, [$"{artist} – {title}"]);

    private static YtSearchResult Topic(string id, string title, string artist, int seconds, long views = 1000) =>
        new(id, title, artist, seconds, views, $"Provided to YouTube by Some Label {title} · {artist} Some Album ℗ 2020 Some Label ...");

    [Fact]
    public void Search_results_are_read_with_topic_uploads_marked()
    {
        var results = Search();

        Assert.Equal(8, results.Count);
        var first = results[0];
        Assert.Equal(("7DXlY8LhWnI", "Gangsta's Paradise", "Coolio", 242), (first.VideoId, first.Title, first.Channel, first.DurationS));
        Assert.Equal(15_244_301, first.ViewCount);
        Assert.True(first.IsTopic);
        Assert.Equal(["7DXlY8LhWnI", "Tw0_qHNRAEA", "lvc2XEMk2tc", "jKr3SSF2-Bw", "tQvnvnE7HLY"], results.Where(r => r.IsTopic).Select(r => r.VideoId));
    }

    [Fact]
    public void Search_without_entries_finds_nothing() =>
        Assert.Empty(MetadataService.ParseSearch("""{"id": "q", "_type": "playlist", "entries": []}"""));

    [Fact]
    public void Topic_upload_with_the_played_length_is_sure_and_the_closest_length_wins()
    {
        var match = SpotifyYtMatcher.Match(Song("Coolio", "Gangsta's Paradise", 241_000), Search());

        Assert.Equal(SpotifyMatchState.Sure, match.State);
        Assert.Equal("jKr3SSF2-Bw", match.Best!.Video.VideoId);
    }

    [Fact]
    public void Without_a_known_length_the_most_watched_topic_upload_wins()
    {
        var match = SpotifyYtMatcher.Match(Song("Coolio", "Gangsta's Paradise (feat. L.V.)"), Search());

        Assert.Equal(SpotifyMatchState.Sure, match.State);
        Assert.Equal("Tw0_qHNRAEA", match.Best!.Video.VideoId);
    }

    [Fact]
    public void A_topic_upload_of_another_length_is_only_a_suggestion()
    {
        var shortened = Search().Where(r => r.VideoId == "lvc2XEMk2tc");
        var song = Song("Coolio", "Gangsta's Paradise", 241_000);

        var match = SpotifyYtMatcher.Match(song, shortened);

        Assert.Equal(SpotifyMatchState.Unsure, match.State);
        Assert.Equal(["168 s instead of 241 s"], match.Best!.Problems(song));
    }

    [Fact]
    public void Music_videos_are_suggestions_with_the_fitting_length_first()
    {
        var videos = Search().Where(r => !r.IsTopic);
        var song = Song("Coolio", "Gangsta's Paradise", 241_000);

        var match = SpotifyYtMatcher.Match(song, videos);

        Assert.Equal(SpotifyMatchState.Unsure, match.State);
        Assert.Equal(["rft8hYQ31Ro", "ky1FT0kHLJ8", "fPO76Jlnz6c"], match.Candidates.Select(c => c.Video.VideoId));
        Assert.Equal(["no Topic upload"], match.Best!.Problems(song));
    }

    [Fact]
    public void Another_version_is_never_a_candidate() =>
        Assert.Equal(SpotifyMatchState.NotFound, SpotifyYtMatcher.Match(Song("Coolio", "Gangsta's Paradise - Sped Up"), Search()).State);

    [Fact]
    public void Another_artist_is_never_a_candidate() =>
        Assert.Equal(SpotifyMatchState.NotFound, SpotifyYtMatcher.Match(Song("Kendrick Lamar", "Gangsta's Paradise"), Search()).State);

    [Fact]
    public void A_remix_needs_the_same_remixer()
    {
        var song = Song("Elton John", "Cold Heart - PNAU Remix", 202_000);
        YtSearchResult[] results =
        [
            Topic("claptone000", "Cold Heart (Claptone Remix)", "Elton John", 202, views: 9_000_000),
            Topic("pnau0000000", "Cold Heart (PNAU Remix)", "Elton John", 203),
            Topic("original000", "Cold Heart", "Elton John", 202, views: 50_000_000),
        ];

        var match = SpotifyYtMatcher.Match(song, results);

        Assert.Equal(SpotifyMatchState.Sure, match.State);
        Assert.Equal("pnau0000000", match.Best!.Video.VideoId);
    }

    [Fact]
    public void A_topic_upload_names_all_artists_of_a_collaboration()
    {
        var song = Song("Macklemore & Ryan Lewis", "Can't Hold Us (feat. Ray Dalton)", 258_000);
        YtSearchResult[] results = [new("canthold000", "Can't Hold Us (feat. Ray Dalton)", "Macklemore & Ryan Lewis - Topic", 258, 1,
            "Provided to YouTube by Macklemore LLC Can't Hold Us (feat. Ray Dalton) · Macklemore & Ryan Lewis · Macklemore · Ryan Lewis ...")];

        Assert.Equal(SpotifyMatchState.Sure, SpotifyYtMatcher.Match(song, results).State);
    }

    [Theory]
    [InlineData("I Ain't Worried (Collins String Version)", "version: collins string")]
    [InlineData("I Ain't Worried (Alternative Version)", "version: alternative")]
    public void Topic_upload_of_another_arrangement_is_only_a_suggestion(string title, string problem)
    {
        var song = Song("OneRepublic", "I Ain't Worried", 148_000);

        var match = SpotifyYtMatcher.Match(song, [Topic("worried0000", title, "OneRepublic", 148)]);

        Assert.Equal(SpotifyMatchState.Unsure, match.State);
        Assert.Contains(problem, match.Best!.Problems(song));
    }

    [Fact]
    public void Topic_upload_with_other_guests_is_only_a_suggestion()
    {
        var song = Song("B.o.B", "Nothin' on You (feat. Bruno Mars)", 268_000);

        var match = SpotifyYtMatcher.Match(song, [Topic("nothin00000", "Nothin' on You (feat. Jay Park)", "B.o.B", 266)]);

        Assert.Equal(SpotifyMatchState.Unsure, match.State);
        Assert.Contains("version: other guests", match.Best!.Problems(song));
    }

    [Fact]
    public void A_topic_channel_counts_without_the_usual_description()
    {
        var song = Song("BLACKPINK", "As If It's Your Last", 213_000);
        var video = new YtSearchResult("aiiyl000000", "AS IF IT'S YOUR LAST", "BLACKPINK - Topic", 214, 1, "YG Entertainment presents ...");

        Assert.Equal(SpotifyMatchState.Sure, SpotifyYtMatcher.Match(song, [video]).State);
    }

    [Fact]
    public void Japan_version_is_another_version() =>
        Assert.Equal(SpotifyMatchState.NotFound,
            SpotifyYtMatcher.Match(Song("BLACKPINK", "How You Like That", 181_000), [Topic("hylt0000000", "How You Like That (Japan Version)", "BLACKPINK", 181)]).State);

    [Fact]
    public void A_guest_named_in_the_title_counts_as_artist()
    {
        var song = Song("mgk", "Home (with Machine Gun Kelly, X Ambassadors & Bebe Rexha)", 204_000);
        var video = new YtSearchResult("home0000000", "Home (with Machine Gun Kelly, X Ambassadors & Bebe Rexha)", "Machine Gun Kelly", 204, 1,
            "Provided to YouTube by Atlantic Records Home · Machine Gun Kelly · X Ambassadors · Bebe Rexha Bright ...");

        Assert.Equal(SpotifyMatchState.Sure, SpotifyYtMatcher.Match(song, [video]).State);
    }

    [Fact]
    public void Full_video_json_is_read_as_search_result()
    {
        var result = MetadataService.ParseResult(
            """{"id": "right000000", "title": "Make You Say", "channel": "Zedd", "duration": 206, "view_count": 1234, "artists": ["Zedd", "Maren Morris"], "description": "Provided to YouTube by Universal Music Group\n\nMake You Say · Zedd"}""");

        Assert.Equal(("right000000", "Make You Say", "Zedd", 206, 1234L), (result.VideoId, result.Title, result.Channel, result.DurationS!.Value, result.ViewCount!.Value));
        Assert.True(result.IsTopic);
        Assert.Equal(["Zedd", "Maren Morris"], result.Artists);
    }

    [Theory]
    [InlineData("Coolio", "Gangsta's Paradise (feat. L.V.)", "Coolio Gangsta's Paradise topic")]
    [InlineData("K.Flay", "Can't Sleep - Vanic Remix", "K.Flay Can't Sleep vanic remix topic")]
    [InlineData("Macklemore & Ryan Lewis", "Can't Hold Us", "Macklemore Can't Hold Us topic")]
    public void Search_query_names_main_artist_song_and_version(string artist, string title, string query) =>
        Assert.Equal(query, SpotifyYtMatcher.SearchQuery(Song(artist, title)));

    [Fact]
    public void Selection_leaves_out_often_skipped_songs()
    {
        static SpotifyStream Play(string title, int seconds) =>
            new(DateTimeOffset.Parse("2025-01-01T12:00:00Z"), $"spotify:track:{title}", "Coolio", title, null, seconds * 1000, "clickrow", "trackdone", false);
        var streams = new[] { Play("Kept", 200), Play("Kept", 200), Play("Filler", 200), Play("Filler", 5), Play("Filler", 5) };

        Assert.Equal(["Kept"], new SpotifySelection(MaxSkipShare: 0.6).Apply(streams).Select(s => s.Title));
        Assert.Equal(2, new SpotifySelection().Apply(streams).Count);
    }

    [Fact]
    public async Task Searches_are_stored_with_their_results_and_decisions()
    {
        var song = Song("Coolio", "Gangsta's Paradise", 241_000);
        await this.matches.SaveSearchAsync(song.Key, "Coolio Gangsta's Paradise topic", Search());

        var record = (await this.matches.ListAsync())[song.Key];
        Assert.Equal(8, record.Results.Count);
        Assert.True(record.Results[0].IsTopic);
        Assert.Equal(SpotifyMatchState.Sure, record.State(song));
        Assert.Equal("jKr3SSF2-Bw", record.VideoToImport(song));

        await this.matches.SetDecisionAsync(song.Key, SpotifyMatchState.Rejected);
        record = (await this.matches.ListAsync())[song.Key];
        Assert.Equal(SpotifyMatchState.Rejected, record.State(song));
        Assert.Null(record.VideoToImport(song));

        // a new search keeps the decision
        await this.matches.SaveSearchAsync(song.Key, "again", Search());
        Assert.Equal(SpotifyMatchState.Rejected, (await this.matches.ListAsync())[song.Key].Decision);

        await this.matches.SetDecisionAsync(song.Key, SpotifyMatchState.Accepted, "rft8hYQ31Ro");
        Assert.Equal("rft8hYQ31Ro", (await this.matches.ListAsync())[song.Key].VideoToImport(song));
    }

    [Fact]
    public async Task A_deleted_track_unlinks_its_song()
    {
        var song = Song("Coolio", "Gangsta's Paradise");
        var id = await this.tracks.AddAsync("jKr3SSF2-Bw", VideoId.WatchUrl("jKr3SSF2-Bw"));
        await this.matches.SaveSearchAsync(song.Key, "q", Search());
        await this.matches.SetTrackAsync(song.Key, id, "jKr3SSF2-Bw");
        Assert.Equal(id, (await this.matches.ListAsync())[song.Key].TrackId);

        await this.tracks.DeleteAsync(id);

        var record = (await this.matches.ListAsync())[song.Key];
        Assert.Null(record.TrackId);
        Assert.Equal(SpotifyMatchState.Accepted, record.Decision);
    }

    [Fact]
    public async Task Search_skips_songs_searched_before_and_stops_when_throttled()
    {
        var known = Song("Coolio", "Gangsta's Paradise");
        await this.matches.SaveSearchAsync(known.Key, "q", Search());
        // two calls per song without a music hit: the song search and the video search
        var search = new FakeSearch(throttleAfter: 4);
        var service = new SpotifySearchService(search, this.matches, TimeSpan.Zero);
        SpotifySong[] songs = [known, Song("Kendrick Lamar", "Alright"), Song("Kendrick Lamar", "HUMBLE."), Song("Kendrick Lamar", "DNA.")];

        var summary = await service.SearchAsync(songs);

        Assert.Equal(["music: Kendrick Lamar Alright", "video: Kendrick Lamar Alright topic", "music: Kendrick Lamar HUMBLE.",
            "video: Kendrick Lamar HUMBLE. topic", "music: Kendrick Lamar DNA."], search.Calls);
        Assert.Equal(new SpotifySearchSummary(Searched: 2, Sure: 0, Unsure: 0, NotFound: 2, Failed: 0, Throttled: true), summary);
        Assert.Equal(["Kendrick Lamar DNA."], (await service.PendingAsync(songs)).Select(s => $"{s.Artist} {s.Title}"));
    }

    [Fact]
    public async Task Search_fetches_fitting_music_songs_until_one_is_sure()
    {
        var song = Song("Zedd", "Make You Say", 206_000);
        var search = new FakeSearch(throttleAfter: 100)
        {
            Music = [new("remix000000", "Make You Say (ellis Remix)", null, null, null, null), new("other000000", "Make You Say", null, null, null, null),
                new("right000000", "Make You Say", null, null, null, null)],
            Fetched =
            {
                ["other000000"] = Topic("other000000", "Make You Say", "Someone Else", 190),
                ["right000000"] = Topic("right000000", "Make You Say", "Zedd", 206) with { Artists = ["Zedd", "Maren Morris", "BEAUZ"] },
            },
        };

        await new SpotifySearchService(search, this.matches, TimeSpan.Zero).SearchAsync([song]);

        Assert.Equal(["music: Zedd Make You Say", "fetch: other000000", "fetch: right000000"], search.Calls);
        var record = (await this.matches.ListAsync())[song.Key];
        Assert.Equal("right000000", record.VideoToImport(song));
        Assert.Equal(["Zedd", "Maren Morris", "BEAUZ"], record.Results[1].Artists);
    }

    [Fact]
    public async Task An_age_restricted_hit_is_skipped_without_stopping_the_search()
    {
        var song = Song("Zedd", "Make You Say", 206_000);
        var search = new FakeSearch(throttleAfter: 100)
        {
            Music = [new("restricted0", "Make You Say", null, null, null, null), new("right000000", "Make You Say", null, null, null, null)],
            Fetched = { ["right000000"] = Topic("right000000", "Make You Say", "Zedd", 206) },
        };

        var summary = await new SpotifySearchService(search, this.matches, TimeSpan.Zero).SearchAsync([song]);

        Assert.Equal(["music: Zedd Make You Say", "fetch: restricted0", "fetch: right000000"], search.Calls);
        Assert.Equal(new SpotifySearchSummary(Searched: 1, Sure: 1, Unsure: 0, NotFound: 0, Failed: 0, Throttled: false), summary);
    }

    [Theory]
    [InlineData("ERROR: [youtube] MaB9uzef3ko: Sign in to confirm your age. This video may be inappropriate for some users.", false)]
    [InlineData("ERROR: [youtube] MaB9uzef3ko: Video unavailable", false)]
    [InlineData("ERROR: [youtube] MaB9uzef3ko: Sign in to confirm you’re not a bot. Use --cookies-from-browser", true)]
    [InlineData("ERROR: [youtube:search] HTTP Error 429: Too Many Requests", true)]
    public void Only_rate_limits_and_bot_checks_count_as_throttling(string error, bool throttled) =>
        Assert.Equal(throttled, MetadataService.IsThrottled(error));

    [Fact]
    public async Task Import_adds_the_chosen_songs_to_a_playlist_in_play_order()
    {
        // known videos only, so yt-dlp is never started
        var before = await this.tracks.AddAsync("alright0000", VideoId.WatchUrl("alright0000"));
        var paradise = await this.tracks.AddAsync("jKr3SSF2-Bw", VideoId.WatchUrl("jKr3SSF2-Bw"));
        var trackImporter = new TrackImportService(this.tracks, new MetadataService(new ToolService(this.paths)),
            new ThumbnailService(this.paths, new HttpClient()));
        var service = new SpotifyImportService(this.tracks, this.playlists, this.matches, trackImporter);
        var top = Song("Coolio", "Gangsta's Paradise", realPlays: 50);
        var second = Song("Kendrick Lamar", "Alright", realPlays: 20);
        await this.matches.SaveSearchAsync(top.Key, "q", Search());

        var result = await service.ImportAsync([new(top, null, "jKr3SSF2-Bw"), new(second, before, null)], "Spotify ≥ 10 Plays");

        Assert.Empty(result.Errors);
        Assert.Equal((2, 0), (result.Added, result.NewTracks));
        Assert.Equal([paradise, before], (await this.playlists.GetTracksAsync(result.PlaylistId)).Select(t => t.Id));
        var record = (await this.matches.ListAsync())[top.Key];
        Assert.Equal((SpotifyMatchState.Accepted, paradise), (record.Decision!.Value, record.TrackId!.Value));
    }

    [Fact]
    public void Imported_songs_are_found_in_the_library_by_their_track()
    {
        var song = Song("Coolio", "Gangsta's Paradise");
        var searches = new Dictionary<string, SpotifySearchRecord>
        {
            [song.Key] = new(song.Key, "q", [], DateTimeOffset.UnixEpoch, SpotifyMatchState.Accepted, "jKr3SSF2-Bw", 4),
        };
        TrackDisplay[] library = [new() { Id = 4, Artist = "Coolio", Title = "Dangerous Minds Theme" }];

        Assert.Equal(4, SpotifyLibraryMatch.TracksBySongKey(library, searches)[song.Key]);
        Assert.False(SpotifyLibraryMatch.TracksBySongKey(library).ContainsKey(song.Key));
    }

    /// <summary>Answers from fixed lists and throttles from the given call on.</summary>
    private sealed class FakeSearch(int throttleAfter) : IYtSearch
    {
        public List<string> Calls { get; } = [];
        public IReadOnlyList<YtSearchResult> Music { get; init; } = [];
        public Dictionary<string, YtSearchResult> Fetched { get; } = [];

        public Task<IReadOnlyList<YtSearchResult>> SearchAsync(string query, int count, CancellationToken ct = default) =>
            this.Answer($"video: {query}", () => (IReadOnlyList<YtSearchResult>)[]);

        public Task<IReadOnlyList<YtSearchResult>> SearchMusicAsync(string query, int count, CancellationToken ct = default) =>
            this.Answer($"music: {query}", () => this.Music);

        public Task<YtSearchResult> FetchResultAsync(string videoId, CancellationToken ct = default) =>
            this.Answer($"fetch: {videoId}", () => this.Fetched.TryGetValue(videoId, out var result)
                ? result
                : throw new InvalidOperationException($"yt-dlp -J failed: ERROR: [youtube] {videoId}: Sign in to confirm your age."));

        private Task<T> Answer<T>(string call, Func<T> result)
        {
            this.Calls.Add(call);
            if (this.Calls.Count > throttleAfter)
                throw new YtThrottledException("HTTP Error 429: Too Many Requests");
            return Task.FromResult(result());
        }
    }
}
