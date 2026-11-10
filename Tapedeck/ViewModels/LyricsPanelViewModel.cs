using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>A line of the lyrics panel; a pause in the singing shows a note.</summary>
public sealed record LyricLineViewModel(string Text, TimeSpan Time)
{
    public string Shown => this.Text.Length == 0 ? Strings.Lyrics_Pause : this.Text;
}

/// <summary>
/// Timed lyrics that follow the app's player: the line sung now and the next one. Only listens to the player while
/// its page is shown (<see cref="Activate"/>), so pages left behind are not kept alive by it.
/// </summary>
public sealed partial class LyricsPanelViewModel(TrackRowViewModel row, PlayerService player) : ViewModelBase
{
    private IReadOnlyList<LrcLine> lines = [];
    private bool active;

    /// <summary>The lyrics have times, so the panel can follow the song.</summary>
    [ObservableProperty]
    public partial bool IsTimed { get; private set; }

    /// <summary>This track is the one in the player, playing or paused.</summary>
    [ObservableProperty]
    public partial bool IsFollowing { get; private set; }

    /// <summary>The line sung now; before the first line a note.</summary>
    [ObservableProperty]
    public partial LyricLineViewModel? Current { get; private set; }

    [ObservableProperty]
    public partial LyricLineViewModel? Next { get; private set; }

    public void Load(TrackLyrics lyrics)
    {
        this.lines = lyrics.IsSynced && lyrics.Text is { } text ? LrcText.Parse(text) : [];
        this.IsTimed = this.lines.Count > 0;
        this.Current = null;
        this.Next = null;
        this.Update();
    }

    /// <summary>Starts following the player; the page calls it when it is shown.</summary>
    public void Activate()
    {
        if (this.active)
            return;
        this.active = true;
        player.PropertyChanged += this.OnPlayerChanged;
        this.Update();
    }

    /// <summary>Stops following the player; the page calls it when it is left.</summary>
    public void Deactivate()
    {
        if (!this.active)
            return;
        this.active = false;
        player.PropertyChanged -= this.OnPlayerChanged;
    }

    /// <summary>Jumps to the line, and starts the track first when another one or none is playing.</summary>
    [RelayCommand]
    private async Task SeekAsync(LyricLineViewModel? line)
    {
        if (line is null || !this.IsTimed)
            return;
        if (player.CurrentTrackId != row.Id)
        {
            if (!row.CanPlay)
                return;
            await row.TogglePlayCommand.ExecuteAsync(null);
        }
        if (player.CurrentTrackId == row.Id)
            player.Seek(line.Time);
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.Position) or nameof(PlayerService.CurrentTrackId))
            this.Update();
    }

    private void Update()
    {
        this.IsFollowing = player.CurrentTrackId == row.Id;
        if (!this.IsTimed)
            return;

        // not playing: the first two lines as a preview; before the first line: a note, then the first line
        var index = this.IsFollowing ? LrcText.IndexAt(this.lines, player.Position) : 0;
        var current = index < 0 ? new LyricLineViewModel(string.Empty, TimeSpan.Zero) : Line(this.lines[index]);
        var next = index + 1 < this.lines.Count ? Line(this.lines[index + 1]) : null;
        // a new value only when the line changes, so the panel animates once per line
        if (this.Current != current)
            this.Current = current;
        if (this.Next != next)
            this.Next = next;
    }

    private static LyricLineViewModel Line(LrcLine line) => new(line.Text, line.Time);
}
