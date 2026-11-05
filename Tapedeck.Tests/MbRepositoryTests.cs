using Microsoft.Data.Sqlite;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Storing, showing and removing a MusicBrainz match in the database and in <c>tracks_display</c>.</summary>
public sealed class MbRepositoryTests : IAsyncLifetime
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}.db");
    private TrackRepository repo = null!;

    private static readonly MbPick PICK = new(
        "rec-1", "Alright", "Kendrick Lamar", "artist-1", "release-1", "group-1", "To Pimp a Butterfly",
        "Kendrick Lamar", "2015-03-15", 7, 1, 219, 0.97);

    public async Task InitializeAsync()
    {
        var factory = new SqliteConnectionFactory(this.dbPath);
        await new Migrator(factory).MigrateAsync();
        this.repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { this.dbPath, this.dbPath + "-wal", this.dbPath + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    private async Task<long> AddVideoAsync()
    {
        var id = await this.repo.AddAsync("Z-48u_uWMHY", "u");
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata(
            "Z-48u_uWMHY", "Kendrick Lamar - Alright", "KendrickLamarVEVO", "20150630", 415, null, null, null, null,
            "Kendrick Lamar", "Alright", [], "{}"));
        await this.repo.AddTagAsync(id, "Entertainment", TagSource.Yt);
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "Kendrick Lamar - Alright.opus", "opus");
        await this.repo.MarkTagsWrittenAsync(id);
        return id;
    }

    [Fact]
    public async Task New_tracks_are_not_searched_yet()
    {
        var id = await this.AddVideoAsync();
        Assert.Equal(MbMatchState.None, (await this.repo.GetDisplayAsync(id))!.MbState);
        Assert.Equal([id], await this.repo.ListByMbStateAsync(MbMatchState.None));
    }

    [Fact]
    public async Task Applied_match_shows_album_place_and_genres_and_outdates_the_tags()
    {
        var id = await this.AddVideoAsync();
        await this.repo.SaveMbResultAsync(id, MbMatchState.Matched, PICK, ["Hip Hop", "Jazz Rap"], null, MbCandidatesJson.Serialize([PICK]));
        await this.repo.SetCoverAsync(id, "covers/release-1.jpg", "caa");

        var display = (await this.repo.GetDisplayAsync(id))!;
        Assert.Equal(MbMatchState.Matched, display.MbState);
        Assert.Equal("To Pimp a Butterfly", display.Album);
        Assert.Equal(FieldSource.Mb, display.AlbumFrom);
        Assert.Equal("Kendrick Lamar", display.AlbumArtist);
        Assert.Equal(FieldSource.Mb, display.AlbumArtistFrom);
        Assert.Equal(2015, display.Year);
        Assert.Equal(7, display.TrackNo);
        Assert.Equal("Hip Hop;Jazz Rap", display.Genres);
        Assert.Equal(FieldSource.Mb, display.GenresFrom);
        Assert.Equal("covers/release-1.jpg", display.Cover);
        Assert.Equal("rec-1", display.MbRecordingId);
        Assert.True(display.TagsStale);

        var raw = (await this.repo.GetAsync(id))!;
        Assert.Equal([PICK], MbCandidatesJson.Deserialize(raw.MbCandidates));
    }

    [Fact]
    public async Task User_corrections_still_win_over_the_match()
    {
        var id = await this.AddVideoAsync();
        await this.repo.SaveMbResultAsync(id, MbMatchState.Matched, PICK, [], null, null);
        await this.repo.SetUserFieldsAsync(id, new UserFields(null, null, "My Album", null, null, null));

        var display = (await this.repo.GetDisplayAsync(id))!;
        Assert.Equal("My Album", display.Album);
        Assert.Equal(FieldSource.User, display.AlbumFrom);
        Assert.Equal("Alright", display.Title);
    }

    [Fact]
    public async Task Suggestion_keeps_youtube_values_and_rejecting_a_match_removes_everything()
    {
        var id = await this.AddVideoAsync();
        await this.repo.SaveMbResultAsync(id, MbMatchState.Suggested, null, [], 0.8, MbCandidatesJson.Serialize([PICK]));
        var suggested = (await this.repo.GetDisplayAsync(id))!;
        Assert.Equal(MbMatchState.Suggested, suggested.MbState);
        Assert.Null(suggested.Album);
        Assert.False(suggested.TagsStale);
        Assert.Equal([id], await this.repo.ListByMbStateAsync(MbMatchState.Suggested));

        await this.repo.SaveMbResultAsync(id, MbMatchState.Matched, PICK, ["Hip Hop"], null, null);
        await this.repo.SetCoverAsync(id, "covers/release-1.jpg", "caa");
        await this.repo.MarkTagsWrittenAsync(id);
        await this.repo.SaveMbResultAsync(id, MbMatchState.Rejected, null, [], null, null);

        var rejected = (await this.repo.GetDisplayAsync(id))!;
        Assert.Equal(MbMatchState.Rejected, rejected.MbState);
        Assert.Null(rejected.Album);
        Assert.Null(rejected.TrackNo);
        Assert.Equal("Entertainment", rejected.Genres);
        Assert.Equal(FieldSource.Yt, rejected.GenresFrom);
        Assert.Null(rejected.CoverSource);
        Assert.True(rejected.TagsStale);
        // the candidates stay, so the user can still pick one
        Assert.Single(MbCandidatesJson.Deserialize((await this.repo.GetAsync(id))!.MbCandidates));
    }
}
