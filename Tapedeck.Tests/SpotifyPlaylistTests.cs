using System.Text;
using Tapedeck.Core.Models;
using Tapedeck.Core.Spotify;

namespace Tapedeck.Tests;

/// <summary>Reading a public Spotify playlist from its embed page and turning it into songs for the YouTube search.</summary>
public sealed class SpotifyPlaylistTests
{
    private static readonly string EMBED = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpotifyPlaylist", "embed.html");

    private const string ID = "37i9dQZF1DX7iB3RCnBnN4";

    [Theory]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DX7iB3RCnBnN4")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DX7iB3RCnBnN4?si=a1b2c3d4e5f60718")]
    [InlineData("https://open.spotify.com/intl-de/playlist/37i9dQZF1DX7iB3RCnBnN4")]
    [InlineData("https://open.spotify.com/embed/playlist/37i9dQZF1DX7iB3RCnBnN4")]
    [InlineData("spotify:playlist:37i9dQZF1DX7iB3RCnBnN4")]
    [InlineData("  open.spotify.com/playlist/37i9dQZF1DX7iB3RCnBnN4  ")]
    public void ReadsThePlaylistIdOfALink(string link) => Assert.Equal(ID, SpotifyPlaylistParser.PlaylistId(link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://open.spotify.com/album/37i9dQZF1DX7iB3RCnBnN4")]
    [InlineData("https://open.spotify.com/playlist/tooshort")]
    [InlineData("https://www.youtube.com/playlist?list=PL37i9dQZF1DX7iB3RCnBnN4")]
    public void RejectsOtherLinks(string? link) => Assert.Null(SpotifyPlaylistParser.PlaylistId(link));

    [Fact]
    public void ParsesTheEmbedPage()
    {
        var playlist = SpotifyPlaylistParser.ParseEmbed(File.ReadAllText(EMBED));

        Assert.NotNull(playlist);
        Assert.Equal(ID, playlist.Id);
        Assert.Equal("BILLIONS CLUB", playlist.Name);
        Assert.Equal("Spotify", playlist.Owner);
        Assert.Equal(5, playlist.Tracks.Count);
        Assert.Equal(new SpotifyPlaylistTrack("spotify:track:0DiWol3AO6WpXZgp0goxAV", "One More Time", "Daft Punk", 320357), playlist.Tracks[0]);
        Assert.Equal("P!nk, Nate Ruess", playlist.Tracks[1].Artists);
        Assert.False(playlist.IsCut);
    }

    [Fact]
    public void AnUnknownPlaylistIsNoPlaylist()
    {
        const string html = """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"status":404,"title":"Page not found"}}}</script>""";
        Assert.Null(SpotifyPlaylistParser.ParseEmbed(html));
    }

    [Fact]
    public void AChangedPageIsAnError()
    {
        Assert.Throws<FormatException>(() => SpotifyPlaylistParser.ParseEmbed("<html><body>Spotify</body></html>"));
        Assert.Throws<FormatException>(() => SpotifyPlaylistParser.ParseEmbed(
            """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"state":{"data":{"entity":{"type":"album"}}}}}}</script>"""));
    }

    [Fact]
    public void ReadsTheTrackCountOfThePlaylistPage()
    {
        const string state = """{"playlistV2":{"content":{"items":[],"pagingInfo":{"nextOffset":30},"totalCount":1447}}}""";
        var html = $"""<script id="initialState" type="text/plain">{Convert.ToBase64String(Encoding.UTF8.GetBytes(state))}</script>""";

        Assert.Equal(1447, SpotifyPlaylistParser.ParseTotalTracks(html));
        Assert.Null(SpotifyPlaylistParser.ParseTotalTracks("<html></html>"));
        Assert.Null(SpotifyPlaylistParser.ParseTotalTracks("""<script id="initialState" type="text/plain">not base64!</script>"""));
    }

    [Theory]
    [InlineData(100, null, true)]
    [InlineData(99, null, false)]
    [InlineData(100, 363, true)]
    [InlineData(100, 100, false)]
    [InlineData(5, 5, false)]
    public void KnowsWhenSpotifyLeftOutTracks(int read, int? total, bool cut)
    {
        var tracks = Enumerable.Range(0, read).Select(i => new SpotifyPlaylistTrack($"spotify:track:{i}", $"Song {i}", "Artist", 200000)).ToList();
        Assert.Equal(cut, new SpotifyPlaylist(ID, "Test", null, tracks, total).IsCut);
    }

    [Fact]
    public void TurnsTheTracksIntoSongsInTheirOrder()
    {
        var songs = SpotifyPlaylistParser.Songs(SpotifyPlaylistParser.ParseEmbed(File.ReadAllText(EMBED))!);

        Assert.Equal(["One More Time", "Just Give Me a Reason (feat. Nate Ruess)", "Creep", "One Of The Girls (with JENNIE, Lily Rose Depp)", "Starboy"],
            songs.Select(s => s.Title));
        var song = songs[1];
        Assert.Equal(SpotifyAnalyzer.SongKey("P!nk", "Just Give Me a Reason"), song.Key);
        Assert.Equal(242721, song.EstimatedDurationMs);
        Assert.Equal(["spotify:track:1mKXFLRA179hdOWQBwUk9e"], song.TrackUris);
        Assert.Equal(0, song.RealPlays);
        // the YouTube search asks for the main artist only
        Assert.Equal("P!nk Just Give Me a Reason", SpotifyYtMatcher.MusicQuery(song));
    }

    [Fact]
    public void ABandWithASlashStaysWhole()
    {
        var playlist = new SpotifyPlaylist(ID, "Test", null, [new SpotifyPlaylistTrack("spotify:track:a", "Thunderstruck", "AC/DC", 292000)], null);

        var song = SpotifyPlaylistParser.Songs(playlist)[0];

        Assert.Equal("AC/DC Thunderstruck", SpotifyYtMatcher.MusicQuery(song));
        Assert.NotEqual(SpotifyAnalyzer.SongKey("AC", "Thunderstruck"), song.Key);
    }

    [Fact]
    public void TakesASongOnceAtItsFirstPlace()
    {
        var playlist = new SpotifyPlaylist(ID, "Test", null,
        [
            new SpotifyPlaylistTrack("spotify:track:a", "Song A", "Artist", 200000),
            new SpotifyPlaylistTrack("spotify:track:b", "Song B", "Artist", 180000),
            new SpotifyPlaylistTrack("spotify:track:c", "Song A - Remastered 2011", "Artist", 201000),
            new SpotifyPlaylistTrack("spotify:track:d", "Song A - Live", "Artist", 240000),
        ], null);

        var songs = SpotifyPlaylistParser.Songs(playlist);

        Assert.Equal(["Song A", "Song B", "Song A - Live"], songs.Select(s => s.Title));
    }

    [Fact]
    public void ASureTopicUploadMatchesAPlaylistSong()
    {
        var song = SpotifyPlaylistParser.Songs(SpotifyPlaylistParser.ParseEmbed(File.ReadAllText(EMBED))!)[4];
        var topic = new YtSearchResult("abcdefghijk", "Starboy", "The Weeknd - Topic", 231, 1_000_000,
            "Provided to YouTube by Universal Music Group\n\nStarboy · The Weeknd · Daft Punk", ["The Weeknd", "Daft Punk"]);

        var match = SpotifyYtMatcher.Match(song, [topic]);

        Assert.Equal(SpotifyMatchState.Sure, match.State);
    }
}
