using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Filter chips above the track table.</summary>
public enum TrackFilter
{
    All,
    Done,
    Pending,
    Error,
}

/// <summary>Library page: add links, list, filter and download tracks.</summary>
public partial class LibraryViewModel(
    ITrackRepository repository,
    TrackImportService importer,
    DownloadService downloader,
    ToolService tools,
    AppPaths paths,
    IPlatformService platform) : ViewModelBase, ITrackActions
{
    private static readonly TimeSpan ConfirmationDuration = TimeSpan.FromSeconds(3);

    private readonly List<TrackRowViewModel> _all = [];
    private readonly List<string> _activities = [];
    private CancellationTokenSource? _confirmation;
    private Task? _toolsReady;

    /// <summary>Rows that pass the current filter and search.</summary>
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>Shown instead of the table when no row is visible; <c>null</c> otherwise.</summary>
    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    /// <summary>Last error, shown below the link bar.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>What the app is doing right now, or a short confirmation afterwards; <c>null</c> hides the toast.</summary>
    [ObservableProperty]
    public partial string? ActivityText { get; private set; }

    /// <summary>Work is in progress (the record spins).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLinkCommand))]
    public partial string NewLink { get; set; } = "";

    [ObservableProperty]
    public partial TrackFilter Filter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial TrackRowViewModel? SelectedTrack { get; set; }

    partial void OnFilterChanged(TrackFilter value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var tracks = await repository.ListDisplayAsync();
        _all.Clear();
        _all.AddRange(tracks.Select((track, index) => new TrackRowViewModel(track, index + 1, this)));
        OnTracksChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddLink))]
    private async Task AddLinkAsync()
    {
        Notice = null;
        // the field stays editable while tools and metadata load, so import the link that was confirmed
        var link = NewLink;
        try
        {
            await EnsureToolsAsync();
            long id;
            using (BeginActivity(Strings.Activity_FetchingInfo))
                id = await importer.AddAsync(link);
            if (NewLink == link)
                NewLink = "";
            await RefreshRowAsync(id);
            if (_all.Find(r => r.Id == id) is { } row)
                _ = ShowConfirmationAsync(string.Format(Strings.Confirm_Added, row.Title));
        }
        catch (Exception ex)
        {
            Notice = string.Format(Strings.Notice_AddFailed, ex.Message);
            // the track may exist without metadata now
            await LoadAsync();
        }
    }

    private bool CanAddLink() => VideoId.TryParse(NewLink, out _);

    [RelayCommand]
    private async Task PasteLinkAsync()
    {
        if (await platform.GetClipboardTextAsync() is { } text)
            NewLink = text.Trim();
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private async Task DownloadPendingAsync()
    {
        foreach (var row in _all.Where(r => r.Status == DownloadStatus.Pending).ToList())
            await DownloadRowAsync(row);
    }

    private bool HasPending() => _all.Any(r => r.Status == DownloadStatus.Pending);

    Task ITrackActions.OpenOnYoutubeAsync(TrackRowViewModel row) => platform.OpenUrlAsync(row.Url);

    Task ITrackActions.DownloadAsync(TrackRowViewModel row) => DownloadRowAsync(row);

    Task ITrackActions.ShowInFolderAsync(TrackRowViewModel row) =>
        platform.OpenFolderAsync(row.FilePath is { } file
            ? Path.GetDirectoryName(paths.ToLibraryPath(file)) ?? paths.LibraryDir
            : paths.LibraryDir);

    private async Task DownloadRowAsync(TrackRowViewModel row)
    {
        if (row.Status == DownloadStatus.Downloading)
            return;

        Notice = null;
        row.Status = DownloadStatus.Downloading;
        row.Progress = 0;
        try
        {
            await EnsureToolsAsync();
            using (BeginActivity(string.Format(Strings.Activity_Downloading, row.Title)))
                await downloader.DownloadAsync(row.Id, new Progress<double>(p => row.Progress = p));
            _ = ShowConfirmationAsync(string.Format(Strings.Confirm_Downloaded, row.Title));
        }
        catch (Exception ex)
        {
            Notice = string.Format(Strings.Notice_DownloadFailed, row.Title, ex.Message);
        }

        await RefreshRowAsync(row.Id);
    }

    /// <summary>yt-dlp, ffmpeg and Deno are downloaded once on first use.</summary>
    private async Task EnsureToolsAsync()
    {
        try
        {
            if (tools.HasAllTools)
                return;
            using (BeginActivity(Strings.Activity_InstallingTools))
                await (_toolsReady ??= tools.EnsureToolsAsync());
        }
        catch
        {
            _toolsReady = null;
            throw;
        }
    }

    /// <summary>Shows <paramref name="text"/> with a spinning record until the returned handle is disposed.</summary>
    private IDisposable BeginActivity(string text)
    {
        _activities.Add(text);
        UpdateActivity();
        return new ActivityHandle(() =>
        {
            _activities.Remove(text);
            UpdateActivity();
        });
    }

    private void UpdateActivity()
    {
        IsBusy = _activities.Count > 0;
        if (IsBusy)
        {
            _confirmation?.Cancel();
            _confirmation = null;
            ActivityText = _activities[^1];
        }
        else if (_confirmation is null)
        {
            ActivityText = null;
        }
    }

    /// <summary>A short confirmation with a resting record, unless other work is still running.</summary>
    private async Task ShowConfirmationAsync(string text)
    {
        if (IsBusy)
            return;

        _confirmation?.Cancel();
        using var cts = _confirmation = new CancellationTokenSource();
        ActivityText = text;
        try
        {
            await Task.Delay(ConfirmationDuration, cts.Token);
            ActivityText = null;
        }
        catch (OperationCanceledException)
        {
            // replaced by new work or another confirmation
        }
        finally
        {
            if (_confirmation == cts)
                _confirmation = null;
        }
    }

    private sealed class ActivityHandle(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    private async Task RefreshRowAsync(long id)
    {
        if (await repository.GetDisplayAsync(id) is not { } track)
            return;

        if (_all.Find(r => r.Id == id) is { } row)
            row.Update(track);
        else
            _all.Add(new TrackRowViewModel(track, _all.Count + 1, this));
        OnTracksChanged();
    }

    private void OnTracksChanged()
    {
        ApplyFilter();
        UpdateSummary();
        DownloadPendingCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var visible = _all.Where(r => MatchesFilter(r.Status) && (search.Length == 0 || r.Matches(search)));

        var selected = SelectedTrack;
        Tracks.Clear();
        foreach (var row in visible)
            Tracks.Add(row);
        SelectedTrack = selected is not null && Tracks.Contains(selected) ? selected : null;

        EmptyText = Tracks.Count > 0 ? null
            : _all.Count == 0 ? Strings.Library_EmptyNoTracks
            : Strings.Library_EmptyFiltered;
    }

    private bool MatchesFilter(DownloadStatus status) => Filter switch
    {
        TrackFilter.Done => status == DownloadStatus.Done,
        TrackFilter.Pending => status is DownloadStatus.Pending or DownloadStatus.Downloading,
        TrackFilter.Error => status == DownloadStatus.Error,
        _ => true,
    };

    private void UpdateSummary()
    {
        if (_all.Count == 0)
        {
            Summary = Strings.Library_SummaryEmpty;
            return;
        }

        var done = _all.Count(r => r.Status == DownloadStatus.Done);
        var errors = _all.Count(r => r.Status == DownloadStatus.Error);
        var count = string.Format(_all.Count == 1 ? Strings.Library_TrackCount_One : Strings.Library_TrackCount_Other, _all.Count);
        var summary = string.Format(Strings.Library_Summary, count, done);
        Summary = errors > 0 ? string.Format(Strings.Library_SummaryErrors, summary, errors) : summary;
    }
}
