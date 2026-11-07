using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Spotify;
using Tapedeck.Resources;

namespace Tapedeck.ViewModels;

/// <summary>Where a song of the Spotify import stands.</summary>
public enum SpotifyRowGroup
{
    /// <summary>A sure match or a video the user chose: imported unless unticked.</summary>
    Ready,
    /// <summary>Only suggestions: the user picks one or none.</summary>
    Review,
    /// <summary>Nothing found, or the user said none of the suggestions is the song.</summary>
    NotFound,
    /// <summary>Already a track in the library; it goes into the playlist too, unless unticked.</summary>
    InLibrary,
}

/// <summary>A song of the Spotify import with its match.</summary>
public partial class SpotifySongRowViewModel : ViewModelBase
{
    private readonly Func<SpotifySongRowViewModel, string?, Task> decide;
    private readonly Action<SpotifySongRowViewModel> changed;

    public SpotifySongRowViewModel(int rank, SpotifySong song, SpotifyRowGroup group, long? trackId, SpotifyCandidate? video,
        IReadOnlyList<SpotifyCandidate> suggestions, bool included,
        Func<SpotifySongRowViewModel, string?, Task> decide, Action<SpotifySongRowViewModel> changed, Func<string, Task> open)
    {
        this.Rank = rank;
        this.Song = song;
        this.Group = group;
        this.TrackId = trackId;
        this.Video = video is null ? null : new SpotifyCandidateViewModel(song, video, open);
        this.Suggestions = suggestions.Select(s => new SpotifyCandidateViewModel(song, s, open, this.Choose)).ToList();
        this.IsIncluded = included;
        this.decide = decide;
        this.changed = changed;
    }

    public int Rank { get; }

    public SpotifySong Song { get; }

    public SpotifyRowGroup Group { get; }

    public long? TrackId { get; }

    public string Title => this.Song.Title;

    public string Artist => this.Song.Artist;

    public string Plays => string.Format(this.Song.RealPlays == 1 ? Strings.Spotify_Plays_One : Strings.Spotify_Plays_Other, this.Song.RealPlays);

    /// <summary>The video that will be imported, on ready rows.</summary>
    public SpotifyCandidateViewModel? Video { get; }

    /// <summary>The videos to choose from, on rows to review.</summary>
    public IReadOnlyList<SpotifyCandidateViewModel> Suggestions { get; }

    public bool CanInclude => this.Group is SpotifyRowGroup.Ready or SpotifyRowGroup.InLibrary;

    public bool IsReview => this.Group == SpotifyRowGroup.Review;

    /// <summary>A song the user decided about can be handed back to the matcher.</summary>
    public bool CanCheckAgain { get; init; }

    [ObservableProperty]
    public partial bool IsIncluded { get; set; }

    partial void OnIsIncludedChanged(bool value) => this.changed(this);

    /// <summary>None of the suggestions is the song.</summary>
    [RelayCommand]
    private Task RejectAsync() => this.decide(this, null);

    /// <summary>Forgets the user's decision, so the matcher's judgement applies again.</summary>
    [RelayCommand]
    private Task CheckAgainAsync() => this.decide(this, string.Empty);

    private Task Choose(string videoId) => this.decide(this, videoId);
}

/// <summary>A YouTube video found for a song.</summary>
public partial class SpotifyCandidateViewModel(SpotifySong song, SpotifyCandidate candidate, Func<string, Task> open,
    Func<string, Task>? choose = null) : ViewModelBase
{
    public string VideoId => candidate.Video.VideoId;

    public string Title => candidate.Video.Title;

    /// <summary>"Coolio · 4:01".</summary>
    public string Details => string.Join(" · ", new[] { candidate.Video.Channel, Length(candidate.Video.DurationS) }.Where(t => !string.IsNullOrEmpty(t)));

    public bool IsTopic => candidate.Video.IsTopic;

    /// <summary>Why the video is not sure, e.g. "no Topic upload · 3:52 instead of 4:35"; empty on sure videos.</summary>
    public string Problems
    {
        get
        {
            var problems = new List<string>();
            if (!candidate.Video.IsTopic)
                problems.Add(Strings.Spotify_NoTopic);
            if (candidate.TitleScore < SpotifyYtMatcher.SURE_SCORE)
                problems.Add(Strings.Spotify_TitleDiffers);
            if (candidate.DurationFits == false)
                problems.Add(string.Format(Strings.Spotify_OtherLength, Length(candidate.Video.DurationS), Length(song.EstimatedDurationMs / 1000)));
            if (candidate.OtherVersion is { } other)
                problems.Add(other == "other guests" ? Strings.Spotify_OtherGuests : string.Format(Strings.Spotify_OtherVersion, other));
            return string.Join(" · ", problems);
        }
    }

    public bool HasProblems => this.Problems.Length > 0;

    public string OpenLabel => string.Format(Strings.Spotify_OpenVideo, this.Title);

    [RelayCommand]
    private Task OpenAsync() => open(Core.VideoId.WatchUrl(candidate.Video.VideoId));

    /// <summary>Takes this video for the song.</summary>
    [RelayCommand]
    private Task ChooseAsync() => choose?.Invoke(candidate.Video.VideoId) ?? Task.CompletedTask;

    private static string Length(int? seconds) =>
        seconds is { } s ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60) : string.Empty;
}
