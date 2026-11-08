using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Spotify;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Gives the one Spotify import page; it keeps its state, so a search goes on while other pages are shown.</summary>
public delegate SpotifyImportViewModel SpotifyImportFactory();

/// <summary>
/// Imports the most played songs of a Spotify streaming history: choose the history, narrow the selection, search
/// the songs on YouTube (stored, so it continues after a stop or restart), check the unsure ones and add the rest
/// as a new playlist. Nothing is downloaded here; the playlist page does that as for every import.
/// </summary>
public partial class SpotifyImportViewModel(
    IPlatformService platform,
    ITrackRepository tracks,
    ISpotifyMatchRepository matches,
    SpotifySearchService search,
    SpotifyImportService importer,
    ToolSetup toolSetup,
    ActivityService activity,
    NavigationService navigation,
    PlaylistsViewModel playlists,
    LibraryViewModel library) : ViewModelBase
{
    /// <summary>How often the result lists follow a running search.</summary>
    private const int REFRESH_EVERY = 10;

    private SpotifyHistory? history;
    private IReadOnlyList<SpotifySongRowViewModel> rows = [];
    private IReadOnlyList<SpotifySong> pending = [];
    // songs the user unticked; they stay unticked when the lists are rebuilt
    private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
    private CancellationTokenSource? searchCancel;
    private string? suggestedName;

    /// <summary>"63,420 plays from 2018-08-22 to 2026-11-04", or a hint while nothing is chosen.</summary>
    [ObservableProperty]
    public partial string SourceSummary { get; private set; } = Strings.Spotify_NoSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    public partial bool HasHistory { get; private set; }

    [ObservableProperty]
    public partial string MinPlaysText { get; set; } = SpotifySelection.DEFAULT.MinRealPlays!.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Empty for no upper limit; with 40 to 49 the songs between two earlier imports.</summary>
    [ObservableProperty]
    public partial string MaxPlaysText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool LeaveOutSkipped { get; set; } = true;

    /// <summary>"934 songs · 120 already in the library · 790 still to search".</summary>
    [ObservableProperty]
    public partial string SelectionSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(HasReminder))]
    public partial bool IsImporting { get; private set; }

    /// <summary>Found songs that are ticked but not in the library yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReminder))]
    [NotifyPropertyChangedFor(nameof(ReminderText))]
    public partial int ReadyCount { get; private set; }

    /// <summary>
    /// Found songs wait for "Add N songs". Shown app-wide and kept until they are added: a search can run for hours,
    /// and nothing reaches the library before the click.
    /// </summary>
    public bool HasReminder => this.ReadyCount > 0 && !this.IsImporting;

    public string ReminderText => string.Format(Strings.Spotify_Reminder, this.ReadyCount, this.ImportLabel);

    /// <summary>"23 of 790 songs · about 2 h 10 min left", or that all are searched.</summary>
    [ObservableProperty]
    public partial string SearchStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    public partial int PendingCount { get; private set; }

    public bool CanSearch => this.HasHistory && !this.IsSearching && this.PendingCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadyTab))]
    [NotifyPropertyChangedFor(nameof(ReviewTab))]
    [NotifyPropertyChangedFor(nameof(NotFoundTab))]
    [NotifyPropertyChangedFor(nameof(InLibraryTab))]
    [NotifyPropertyChangedFor(nameof(VisibleRows))]
    [NotifyPropertyChangedFor(nameof(IsListEmpty))]
    [NotifyPropertyChangedFor(nameof(CanToggleAll))]
    public partial SpotifyRowGroup Tab { get; set; } = SpotifyRowGroup.Ready;

    public string ReadyTab => this.TabLabel(Strings.Spotify_Ready, SpotifyRowGroup.Ready);
    public string ReviewTab => this.TabLabel(Strings.Spotify_Review, SpotifyRowGroup.Review);
    public string NotFoundTab => this.TabLabel(Strings.Spotify_NotFound, SpotifyRowGroup.NotFound);
    public string InLibraryTab => this.TabLabel(Strings.Spotify_InLibrary, SpotifyRowGroup.InLibrary);

    public IReadOnlyList<SpotifySongRowViewModel> VisibleRows => this.rows.Where(r => r.Group == this.Tab).ToList();

    public bool IsListEmpty => this.VisibleRows.Count == 0;

    /// <summary>Only ready and library songs have a tick.</summary>
    public bool CanToggleAll => this.Tab is SpotifyRowGroup.Ready or SpotifyRowGroup.InLibrary && !this.IsListEmpty;

    [ObservableProperty]
    public partial string PlaylistName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial int ImportCount { get; private set; }

    public string ImportLabel => string.Format(Strings.Spotify_ImportButton, this.ImportCount);

    public bool CanImport => this.ImportCount > 0 && !this.IsImporting && !this.IsSearching;

    /// <summary>Why something did not work, shown below the header.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    partial void OnMinPlaysTextChanged(string value) => _ = this.RefreshAsync();

    partial void OnMaxPlaysTextChanged(string value) => _ = this.RefreshAsync();

    partial void OnLeaveOutSkippedChanged(bool value) => _ = this.RefreshAsync();

    partial void OnImportCountChanged(int value) => this.OnPropertyChanged(nameof(this.ImportLabel));

    [RelayCommand]
    private void Back() => navigation.Back();

    [RelayCommand]
    private void ShowTab(SpotifyRowGroup tab) => this.Tab = tab;

    /// <summary>Ticks every song of the open tab.</summary>
    [RelayCommand]
    private void SelectAll() => this.SetAllIncluded(true);

    /// <summary>Unticks every song of the open tab, e.g. the ones that went into an earlier Spotify playlist.</summary>
    [RelayCommand]
    private void DeselectAll() => this.SetAllIncluded(false);

    private void SetAllIncluded(bool included)
    {
        foreach (var row in this.VisibleRows.Where(r => r.CanInclude))
            row.IsIncluded = included;
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (await platform.PickFolderAsync(Strings.Spotify_PickFolderTitle, null) is { } path)
            await this.LoadAsync(path);
    }

    [RelayCommand]
    private async Task PickZipAsync()
    {
        if (await platform.PickOpenFileAsync(Strings.Spotify_PickZipTitle, new FileFilter(Strings.Spotify_ZipFileType, "zip")) is { } path)
            await this.LoadAsync(path);
    }

    /// <summary>Searches the songs of the selection that have no stored search yet, until done, stopped or throttled.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        this.Notice = null;
        try
        {
            await toolSetup.EnsureAsync();
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Spotify_SearchFailed, ex.Message);
            return;
        }

        this.IsSearching = true;
        this.searchCancel = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        try
        {
            using var progress = activity.Begin(string.Format(Strings.Spotify_Searching, 0, this.pending.Count));
            var summary = await search.SearchAsync(this.pending, new Progress<(int Done, int Total)>(p =>
            {
                progress.Update(string.Format(Strings.Spotify_Searching, p.Done, p.Total));
                this.SearchStatus = Status(p.Done, p.Total, started);
                if (p.Done > 0 && p.Done % REFRESH_EVERY == 0)
                    _ = this.RefreshAsync();
            }), ct: this.searchCancel.Token);
            if (summary.Throttled)
                this.Notice = Strings.Spotify_Throttled;
            else if (summary.Failed > 0)
                this.Notice = string.Format(Strings.Spotify_SearchSomeFailed, summary.Failed);
        }
        catch (OperationCanceledException)
        {
            // stopped by the user; everything searched so far is stored
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Spotify_SearchFailed, ex.Message);
        }
        finally
        {
            this.searchCancel.Dispose();
            this.searchCancel = null;
            this.IsSearching = false;
            await this.RefreshAsync();
        }
    }

    [RelayCommand]
    private void StopSearch() => this.searchCancel?.Cancel();

    /// <summary>Adds the ready songs to the library and creates the playlist with them and the ticked library songs.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        this.Notice = null;
        var name = this.PlaylistName.Trim();
        if (await playlists.ValidateNameAsync(name, exceptId: null) is { } error)
        {
            this.Notice = error;
            return;
        }

        var entries = this.rows
            .Where(r => r.CanInclude && r.IsIncluded)
            .OrderBy(r => r.Rank)
            .Select(r => new SpotifyImportEntry(r.Song, r.TrackId, r.TrackId is null ? r.Video?.VideoId : null))
            .ToList();
        if (entries.Count == 0)
            return;

        this.IsImporting = true;
        SpotifyImportResult? result = null;
        try
        {
            await toolSetup.EnsureAsync();
            using var progress = activity.Begin(string.Format(Strings.Spotify_Importing, 0, entries.Count));
            result = await importer.ImportAsync(entries, name, new Progress<(int Done, int Total)>(p =>
                progress.Update(string.Format(Strings.Spotify_Importing, p.Done, p.Total))));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Spotify_ImportFailed, ex.Message);
        }
        finally
        {
            this.IsImporting = false;
            // also after a failure: some tracks may be in the library already
            await library.AddNewTracksAsync();
            await playlists.ChangedAsync();
            await this.RefreshAsync();
        }

        if (result is null)
            return;
        if (result.Errors.Count > 0)
            this.Notice = string.Format(Strings.Spotify_SongsFailed, result.Errors.Count,
                string.Join("; ", result.Errors.Take(3).Select(e => $"{e.Song.Artist} – {e.Song.Title}: {e.Message}"))
                + (result.Errors.Count > 3 ? " …" : string.Empty));
        activity.Confirm(string.Format(Strings.Spotify_Imported, name, result.Added));
        playlists.OpenPlaylist(result.PlaylistId);
    }

    private async Task LoadAsync(string path)
    {
        this.Notice = null;
        try
        {
            using (activity.Begin(Strings.Spotify_Reading))
                this.history = await Task.Run(() => new SpotifyHistoryReader().ReadAsync(path));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Spotify_ReadFailed, ex.Message);
            return;
        }

        var streams = this.history.Streams;
        this.SourceSummary = streams.Count == 0
            ? Strings.Spotify_NoSource
            : string.Format(Strings.Spotify_SourceSummary, streams.Count.ToString("N0", CultureInfo.CurrentCulture),
                streams[0].EndedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                streams[^1].EndedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        this.HasHistory = streams.Count > 0;
        this.excluded.Clear();
        await this.RefreshAsync();
    }

    /// <summary>Rebuilds the selection and the result lists from the history, the library and the stored searches.</summary>
    private async Task RefreshAsync()
    {
        if (this.history is null)
            return;

        var minPlays = int.TryParse(this.MinPlaysText, NumberStyles.None, CultureInfo.InvariantCulture, out var min) && min > 0 ? min : 1;
        int? maxPlays = int.TryParse(this.MaxPlaysText, NumberStyles.None, CultureInfo.InvariantCulture, out var max) && max >= minPlays ? max : null;
        var selection = new SpotifySelection(MinRealPlays: minPlays, MaxRealPlays: maxPlays,
            MaxSkipShare: this.LeaveOutSkipped ? SpotifySelection.DEFAULT.MaxSkipShare : null);
        var songs = selection.Apply(this.history.Streams);
        var searches = await matches.ListAsync();
        var inLibrary = SpotifyLibraryMatch.TracksBySongKey(await tracks.ListDisplayAsync(), searches);

        var built = new List<SpotifySongRowViewModel>();
        var waiting = new List<SpotifySong>();
        for (var i = 0; i < songs.Count; i++)
        {
            var song = songs[i];
            if (inLibrary.TryGetValue(song.Key, out var trackId))
            {
                built.Add(this.Row(i + 1, song, SpotifyRowGroup.InLibrary, trackId, null, [], decided: false));
                continue;
            }
            if (!searches.TryGetValue(song.Key, out var stored))
            {
                waiting.Add(song);
                continue;
            }

            var match = SpotifyYtMatcher.Match(song, stored.Results);
            var decided = stored.Decision is not null;
            switch (stored.Decision ?? match.State)
            {
                case SpotifyMatchState.Accepted:
                    var chosen = stored.Results.FirstOrDefault(r => r.VideoId == stored.VideoId);
                    var candidate = chosen is null ? null : match.Candidates.FirstOrDefault(c => c.Video.VideoId == chosen.VideoId)
                                                            ?? new SpotifyCandidate(chosen, 1, 1, null, null);
                    built.Add(this.Row(i + 1, song, candidate is null ? SpotifyRowGroup.NotFound : SpotifyRowGroup.Ready, null, candidate, [], decided));
                    break;
                case SpotifyMatchState.Sure:
                    built.Add(this.Row(i + 1, song, SpotifyRowGroup.Ready, null, match.Best, [], decided));
                    break;
                case SpotifyMatchState.Unsure:
                    built.Add(this.Row(i + 1, song, SpotifyRowGroup.Review, null, null, match.Candidates, decided));
                    break;
                default:
                    built.Add(this.Row(i + 1, song, SpotifyRowGroup.NotFound, null, null, [], decided));
                    break;
            }
        }

        this.rows = built;
        this.pending = waiting;
        this.PendingCount = waiting.Count;
        this.SelectionSummary = string.Format(Strings.Spotify_SelectionSummary, songs.Count,
            built.Count(r => r.Group == SpotifyRowGroup.InLibrary), waiting.Count);
        if (!this.IsSearching)
            this.SearchStatus = waiting.Count == 0 ? Strings.Spotify_SearchDone : string.Empty;
        this.UpdateCounts();
        foreach (var name in new[] { nameof(this.ReadyTab), nameof(this.ReviewTab), nameof(this.NotFoundTab), nameof(this.InLibraryTab), nameof(this.VisibleRows), nameof(this.IsListEmpty), nameof(this.CanToggleAll) })
            this.OnPropertyChanged(name);

        // a name the user did not change follows the selection
        var suggestion = await playlists.FreeNameAsync(maxPlays is { } upper
            ? string.Format(Strings.Spotify_RangePlaylistName, minPlays, upper)
            : string.Format(Strings.Spotify_DefaultPlaylistName, minPlays));
        if (this.PlaylistName.Length == 0 || this.PlaylistName == this.suggestedName)
            this.PlaylistName = suggestion;
        this.suggestedName = suggestion;
    }

    private SpotifySongRowViewModel Row(int rank, SpotifySong song, SpotifyRowGroup group, long? trackId, SpotifyCandidate? video,
        IReadOnlyList<SpotifyCandidate> suggestions, bool decided) =>
        new(rank, song, group, trackId, video, suggestions, !this.excluded.Contains(song.Key), this.DecideAsync, this.IncludedChanged, platform.OpenUrlAsync)
        {
            CanCheckAgain = decided,
        };

    /// <param name="videoId">The chosen video, <c>null</c> for none of them, empty to forget the decision.</param>
    private async Task DecideAsync(SpotifySongRowViewModel row, string? videoId)
    {
        await (videoId switch
        {
            null => matches.SetDecisionAsync(row.Song.Key, SpotifyMatchState.Rejected),
            "" => matches.SetDecisionAsync(row.Song.Key, null),
            _ => matches.SetDecisionAsync(row.Song.Key, SpotifyMatchState.Accepted, videoId),
        });
        await this.RefreshAsync();
    }

    private void IncludedChanged(SpotifySongRowViewModel row)
    {
        if (row.IsIncluded)
            this.excluded.Remove(row.Song.Key);
        else
            this.excluded.Add(row.Song.Key);
        this.UpdateCounts();
    }

    private void UpdateCounts()
    {
        this.ImportCount = this.rows.Count(r => r.CanInclude && r.IsIncluded);
        this.ReadyCount = this.rows.Count(r => r.Group == SpotifyRowGroup.Ready && r.IsIncluded);
        this.OnPropertyChanged(nameof(this.ReminderText));
    }

    private string TabLabel(string format, SpotifyRowGroup group) => string.Format(format, this.rows.Count(r => r.Group == group));

    /// <summary>"23 of 790 songs · about 2 h 10 min left", from the pace so far.</summary>
    private static string Status(int done, int total, DateTime started)
    {
        if (done == 0)
            return string.Format(Strings.Spotify_SearchProgress, done, total, "…");
        var left = TimeSpan.FromTicks((DateTime.UtcNow - started).Ticks / done * (total - done));
        var text = left.TotalHours >= 1
            ? string.Format(Strings.Spotify_Hours, (int)left.TotalHours, left.Minutes)
            : string.Format(Strings.Spotify_Minutes, Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
        return string.Format(Strings.Spotify_SearchProgress, done, total, text);
    }
}
