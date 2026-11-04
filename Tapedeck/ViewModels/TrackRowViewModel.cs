using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;

namespace Tapedeck.ViewModels;

/// <summary>Actions a row offers in its menu, carried out by the page that owns the rows.</summary>
public interface ITrackActions
{
    Task DownloadAsync(TrackRowViewModel row);

    Task OpenOnYoutubeAsync(TrackRowViewModel row);

    Task ShowInFolderAsync(TrackRowViewModel row);

    void OpenDetails(TrackRowViewModel row);

    /// <summary>Reloads the row after its values changed elsewhere, e.g. on the detail page.</summary>
    Task RefreshAsync(TrackRowViewModel row);
}

/// <summary>One row of the library table, built from <see cref="TrackDisplay"/>.</summary>
public partial class TrackRowViewModel : ViewModelBase
{
    private readonly ITrackActions _actions;

    public TrackRowViewModel(TrackDisplay track, int number, ITrackActions actions)
    {
        _actions = actions;
        Number = number;
        Update(track);
    }

    public long Id { get; private set; }

    public string Url { get; private set; } = "";

    /// <summary>Audio file relative to the library folder, once downloaded.</summary>
    public string? FilePath { get; private set; }

    public int Number { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Artist { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumText), nameof(HasAlbum))]
    public partial string? Album { get; private set; }

    [ObservableProperty]
    public partial string DurationText { get; private set; } = "";

    [ObservableProperty]
    public partial int? Rating { get; private set; }

    [ObservableProperty]
    public partial DownloadStatus Status { get; set; }

    /// <summary>Download progress from 0 to 1 while <see cref="Status"/> is downloading.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? ThumbPath { get; private set; }

    /// <summary>Missing albums show a dash instead of an empty cell.</summary>
    public string AlbumText => Album ?? "—";

    public bool HasAlbum => Album is not null;

    public void Update(TrackDisplay track)
    {
        Id = track.Id;
        Url = track.Url.Length > 0 ? track.Url : VideoId.WatchUrl(track.VideoId);
        Title = track.Title;
        Artist = track.Artist;
        Album = string.IsNullOrWhiteSpace(track.Album) ? null : track.Album;
        DurationText = FormatDuration(track.DurationS);
        Rating = track.Rating;
        Status = track.DownloadStatus;
        Progress = 0;
        ThumbPath = track.ThumbPath;
        FilePath = track.FilePath;
    }

    [RelayCommand]
    private Task DownloadAsync() => _actions.DownloadAsync(this);

    [RelayCommand]
    private Task OpenOnYoutubeAsync() => _actions.OpenOnYoutubeAsync(this);

    [RelayCommand]
    private Task ShowInFolderAsync() => _actions.ShowInFolderAsync(this);

    [RelayCommand]
    private void OpenDetails() => _actions.OpenDetails(this);

    public bool Matches(string search) =>
        Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Artist.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || (Album?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static string FormatDuration(int? seconds)
    {
        if (seconds is not { } s)
            return "";
        var time = TimeSpan.FromSeconds(s);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
