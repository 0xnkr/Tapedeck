using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Resources;

namespace Tapedeck.ViewModels;

/// <summary>Actions a row offers in its menu, carried out by the page that owns the rows.</summary>
public interface ITrackActions
{
    Task DownloadAsync(TrackRowViewModel row);

    Task OpenOnYoutubeAsync(TrackRowViewModel row);

    /// <summary>Plays the track's file, or pauses it when it is playing; does nothing without a file.</summary>
    Task TogglePlayAsync(TrackRowViewModel row);

    Task ShowInFolderAsync(TrackRowViewModel row);

    void OpenDetails(TrackRowViewModel row);

    /// <summary>Reloads the row after its values changed elsewhere, e.g. on the detail page.</summary>
    Task RefreshAsync(TrackRowViewModel row);

    /// <summary>Asks for confirmation, then removes the track.</summary>
    /// <returns><c>true</c> when the track was removed.</returns>
    Task<bool> RemoveAsync(TrackRowViewModel row);

    /// <summary>Asks for confirmation, then keeps this track of its duplicates and removes the others.</summary>
    Task KeepAsync(TrackRowViewModel row);

    void CancelDownload(TrackRowViewModel row);

    /// <summary>The playlists offered in the "add to playlist" menu.</summary>
    IReadOnlyList<PlaylistItemViewModel> Playlists { get; }

    Task AddToPlaylistAsync(TrackRowViewModel row, long playlistId);

    /// <summary>Asks for a name, creates the playlist and adds the track.</summary>
    Task AddToNewPlaylistAsync(TrackRowViewModel row);
}

/// <summary>One entry of the "add to playlist" menu.</summary>
public sealed record PlaylistMenuEntry(string Header, IAsyncRelayCommand Command);

/// <summary>One row of the library table, built from <see cref="TrackDisplay"/>.</summary>
public partial class TrackRowViewModel : ViewModelBase
{
    private readonly ITrackActions actions;

    /// <param name="status">The status to show, which can differ from the stored one (<see cref="DownloadStatus.Missing"/>).</param>
    public TrackRowViewModel(TrackDisplay track, DownloadStatus status, int number, ITrackActions actions)
    {
        this.actions = actions;
        this.Number = number;
        this.Update(track, status);
    }

    public long Id { get; private set; }

    /// <summary>"New playlist …" and all playlists; filled by <see cref="RefreshPlaylistMenu"/> when the menu opens.</summary>
    public ObservableCollection<PlaylistMenuEntry> PlaylistMenu { get; } = [];

    public string Url { get; private set; } = string.Empty;

    /// <summary>Audio file relative to the library folder, once downloaded.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Position in the library, renumbered after a track is removed.</summary>
    [ObservableProperty]
    public partial int Number { get; set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Artist { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumText), nameof(HasAlbum))]
    public partial string? Album { get; private set; }

    [ObservableProperty]
    public partial string DurationText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int? Rating { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading), nameof(CanPlay), nameof(PlayTooltip))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(KeepCommand))]
    public partial DownloadStatus Status { get; set; }

    public bool IsDownloading => this.Status == DownloadStatus.Downloading;

    /// <summary>Download progress from 0 to 1 while <see cref="Status"/> is downloading.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ThumbPath { get; private set; }

    /// <summary>Square cover from MusicBrainz, else the thumbnail; shown in the player bar.</summary>
    public string? CoverPath { get; private set; }

    public int? DurationS { get; private set; }

    /// <summary>The file is there, so the cover plays it.</summary>
    public bool CanPlay => this.FilePath is not null && this.Status == DownloadStatus.Done;

    /// <summary>Loaded in the player, playing or paused; the cover keeps its play mark visible.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayTooltip))]
    public partial bool IsPlaying { get; set; }

    public string PlayTooltip => !this.CanPlay ? Strings.Player_NotDownloaded
        : this.IsPlaying ? Strings.Player_Pause
        : Strings.Player_PlayHint;

    /// <summary>Whether the track is matched with MusicBrainz or waits for the user to confirm a suggestion.</summary>
    [ObservableProperty]
    public partial MbMatchState MbState { get; private set; }

    /// <summary>The file exists and its tags are older than the shown values.</summary>
    public bool TagsStale { get; private set; }

    /// <summary>The MusicBrainz recording, which tells duplicates apart from songs of the same name.</summary>
    public string? MbRecordingId { get; private set; }

    /// <summary>The same song is in the library from another video; the menu offers to keep this one.</summary>
    [ObservableProperty]
    public partial bool IsDuplicate { get; set; }

    /// <summary>Missing albums show a dash instead of an empty cell.</summary>
    public string AlbumText => this.Album ?? "—";

    public bool HasAlbum => this.Album is not null;

    public void Update(TrackDisplay track, DownloadStatus status)
    {
        this.Id = track.Id;
        this.Url = track.Url.Length > 0 ? track.Url : VideoId.WatchUrl(track.VideoId);
        this.Title = track.Title;
        this.Artist = track.Artist;
        this.Album = string.IsNullOrWhiteSpace(track.Album) ? null : track.Album;
        this.DurationText = FormatDuration(track.DurationS);
        this.Rating = track.Rating;
        this.Status = status;
        this.Progress = 0;
        this.ThumbPath = track.ThumbPath;
        this.FilePath = track.FilePath;
        this.CoverPath = track.Cover;
        this.DurationS = track.DurationS;
        this.OnPropertyChanged(nameof(this.CanPlay));
        this.OnPropertyChanged(nameof(this.PlayTooltip));
        this.MbState = track.MbState;
        this.TagsStale = track.TagsStale;
        this.MbRecordingId = track.MbRecordingId;
    }

    [RelayCommand]
    private Task DownloadAsync() => this.actions.DownloadAsync(this);

    [RelayCommand]
    private Task OpenOnYoutubeAsync() => this.actions.OpenOnYoutubeAsync(this);

    [RelayCommand]
    private Task TogglePlayAsync() => this.actions.TogglePlayAsync(this);

    [RelayCommand]
    private Task ShowInFolderAsync() => this.actions.ShowInFolderAsync(this);

    [RelayCommand]
    private void OpenDetails() => this.actions.OpenDetails(this);

    /// <summary>Not while downloading: cancel first, so no half written file stays behind.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task RemoveAsync() => this.actions.RemoveAsync(this);

    private bool CanRemove() => !this.IsDownloading;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task KeepAsync() => this.actions.KeepAsync(this);

    [RelayCommand]
    private void CancelDownload() => this.actions.CancelDownload(this);

    /// <summary>Rebuilds the "add to playlist" menu, so it shows playlists created since the last time.</summary>
    public void RefreshPlaylistMenu()
    {
        this.PlaylistMenu.Clear();
        this.PlaylistMenu.Add(new PlaylistMenuEntry(Strings.Track_NewPlaylist,
            new AsyncRelayCommand(() => this.actions.AddToNewPlaylistAsync(this))));
        // smart playlists pick their tracks themselves
        foreach (var playlist in this.actions.Playlists.Where(p => !p.IsSmart))
        {
            this.PlaylistMenu.Add(new PlaylistMenuEntry(playlist.Name,
                new AsyncRelayCommand(() => this.actions.AddToPlaylistAsync(this, playlist.Id))));
        }
    }

    public bool Matches(string search) =>
        this.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || this.Artist.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || (this.Album?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static string FormatDuration(int? seconds)
    {
        if (seconds is not { } s)
            return string.Empty;
        var time = TimeSpan.FromSeconds(s);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
