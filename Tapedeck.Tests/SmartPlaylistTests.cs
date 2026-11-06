using Microsoft.Data.Sqlite;
using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Smart playlists in the database: their tracks follow the library, nothing is stored per track.</summary>
public sealed class SmartPlaylistTests : IAsyncLifetime
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}.db");
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;

    private static readonly SmartRules TRAP = new(SmartMatch.All,
        [new SmartRule(SmartField.Genre, SmartOperator.Contains, "trap")], SmartSort.Newest, null);

    public async Task InitializeAsync()
    {
        var factory = new SqliteConnectionFactory(this.dbPath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { this.dbPath, this.dbPath + "-wal", this.dbPath + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    private async Task<long> AddAsync(string videoId, string title, params string[] genres)
    {
        var id = await this.tracks.AddAsync(videoId, "u");
        await this.tracks.UpdateYtMetadataAsync(id, new YtMetadata(
            videoId, title, "Channel", "20200101", 200, title, "Artist", null, null, null, null, [], "{}"));
        await this.tracks.SetUserTagsAsync(id, genres);
        return id;
    }

    [Fact]
    public async Task Tracks_follow_the_library()
    {
        var we = await this.AddAsync("aaaaaaaaaaa", "We Dem Boyz", "Trap");
        await this.AddAsync("bbbbbbbbbbb", "Money Trees", "Hip Hop");
        var id = await this.playlists.CreateSmartAsync("Trap", TRAP);

        Assert.Equal([we], (await this.playlists.GetTracksAsync(id)).Select(t => t.Id));

        var later = await this.AddAsync("ccccccccccc", "For Everybody", "Memphis Rap", "Trap");
        Assert.Equal([later, we], (await this.playlists.GetTracksAsync(id)).Select(t => t.Id));

        await this.tracks.SetUserTagsAsync(we, ["Hip Hop"]);
        Assert.Equal([later], (await this.playlists.GetTracksAsync(id)).Select(t => t.Id));
    }

    [Fact]
    public async Task Counts_use_the_rules_and_tracks_cannot_be_added_by_hand()
    {
        var we = await this.AddAsync("aaaaaaaaaaa", "We Dem Boyz", "Trap");
        var money = await this.AddAsync("bbbbbbbbbbb", "Money Trees", "Hip Hop");
        var id = await this.playlists.CreateSmartAsync("Trap", TRAP);

        Assert.Equal(0, await this.playlists.AddTracksAsync(id, [money]));
        var listed = Assert.Single(await this.playlists.ListAsync());
        Assert.True(listed.IsSmart);
        Assert.Equal(1, listed.TrackCount);
        Assert.Equal(1, (await this.playlists.GetAsync(id))!.TrackCount);
        Assert.Equal([we], (await this.playlists.GetTracksAsync(id)).Select(t => t.Id));
    }

    [Fact]
    public async Task Update_changes_name_and_rules_but_never_turns_a_normal_playlist_smart()
    {
        await this.AddAsync("aaaaaaaaaaa", "We Dem Boyz", "Trap");
        var smart = await this.playlists.CreateSmartAsync("Trap", TRAP);
        var normal = await this.playlists.CreateAsync("Mine");

        await this.playlists.UpdateSmartAsync(smart, "Everything", SmartRules.Empty);
        await this.playlists.UpdateSmartAsync(normal, "Mine", TRAP);

        var updated = (await this.playlists.GetAsync(smart))!;
        Assert.Equal("Everything", updated.Name);
        Assert.Empty(SmartRules.FromJson(updated.Rules).Rules);
        Assert.False((await this.playlists.GetAsync(normal))!.IsSmart);
    }
}
