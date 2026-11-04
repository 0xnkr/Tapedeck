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
using Tapedeck.Core.Csv;
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
    ToolSetup toolSetup,
    AppPaths paths,
    IPlatformService platform,
    ActivityService activity,
    NavigationService navigation,
    LibraryFiles files,
    DialogService dialogs,
    PlaylistsViewModel playlists,
    PlaylistSync playlistSync,
    TrackDetailFactory createDetail) : ViewModelBase, ITrackActions
{
    private readonly List<TrackRowViewModel> all = [];
    // one per running download, so a single row can be cancelled
    private readonly Dictionary<long, CancellationTokenSource> running = [];
    // the current "download pending" or "download missing" run
    private CancellationTokenSource? batch;

    /// <summary>Rows that pass the current filter and search.</summary>
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Shown instead of the table when no row is visible; <c>null</c> otherwise.</summary>
    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    /// <summary>Last error, shown below the link bar.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLinkCommand))]
    public partial string NewLink { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TrackFilter Filter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>At least one download runs; the toolbar offers to cancel them.</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; private set; }

    /// <summary>Some downloaded files were deleted or moved outside the app.</summary>
    [ObservableProperty]
    public partial bool HasMissing { get; private set; }

    [ObservableProperty]
    public partial TrackRowViewModel? SelectedTrack { get; set; }

    partial void OnFilterChanged(TrackFilter value) => this.ApplyFilter();

    partial void OnSearchTextChanged(string value) => this.ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var tracks = await repository.ListDisplayAsync();
        this.all.Clear();
        this.all.AddRange(tracks.Select((track, index) => this.NewRow(track, index + 1)));
        this.OnTracksChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddLink))]
    private async Task AddLinkAsync()
    {
        this.Notice = null;
        // the field stays editable while tools and metadata load, so import the link that was confirmed
        var link = this.NewLink;
        if (PlaylistLink.TryParse(link, out var listId))
        {
            // a video opened inside a playlist: only the user knows which of the two is meant
            var wholePlaylist = VideoId.TryParse(link, out _) ? await this.AskWholePlaylistAsync() : true;
            if (wholePlaylist is null)
                return;
            if (wholePlaylist.Value)
            {
                await this.ImportPlaylistAsync(link, listId);
                return;
            }
        }

        try
        {
            await toolSetup.EnsureAsync();
            long id;
            using (activity.Begin(Strings.Activity_FetchingInfo))
                id = await importer.AddAsync(link);
            if (this.NewLink == link)
                this.NewLink = string.Empty;
            await this.RefreshRowAsync(id);
            if (this.all.Find(r => r.Id == id) is { } row)
                activity.Confirm(string.Format(Strings.Confirm_Added, row.Title));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_AddFailed, ex.Message);
            // the track may exist without metadata now
            await this.LoadAsync();
        }
    }

    private bool CanAddLink() => VideoId.TryParse(this.NewLink, out _) || PlaylistLink.TryParse(this.NewLink, out _);

    /// <returns><c>true</c> for the whole playlist, <c>false</c> for the single video, <c>null</c> when cancelled.</returns>
    private async Task<bool?> AskWholePlaylistAsync()
    {
        var answer = await dialogs.ConfirmAsync(new ConfirmRequest(
            Strings.LinkInPlaylist_Title, Strings.LinkInPlaylist_Message, Strings.Library_Add,
            OptionText: Strings.LinkInPlaylist_Option));
        return answer?.Option;
    }

    /// <summary>Reads the playlist, then the playlists page creates or syncs the linked playlist and opens it.</summary>
    private async Task ImportPlaylistAsync(string link, string listId)
    {
        try
        {
            var source = await playlists.FetchPlaylistAsync(listId);
            if (this.NewLink == link)
                this.NewLink = string.Empty;
            await playlists.ImportAsync(source);
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_PlaylistReadFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task PasteLinkAsync()
    {
        if (await platform.GetClipboardTextAsync() is { } text)
            this.NewLink = text.Trim();
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private Task DownloadPendingAsync() => this.DownloadAllAsync(DownloadStatus.Pending);

    private bool HasPending() => this.all.Any(r => r.Status == DownloadStatus.Pending);

    /// <summary>Downloads the tracks whose files were deleted or moved outside the app.</summary>
    [RelayCommand]
    private Task DownloadMissingAsync() => this.DownloadAllAsync(DownloadStatus.Missing);

    /// <summary>Stops the running batch and every single download.</summary>
    [RelayCommand]
    private void CancelDownloads()
    {
        this.batch?.Cancel();
        foreach (var cts in this.running.Values)
            cts.Cancel();
    }

    /// <summary>
    /// Checks again which downloaded files still exist, e.g. when the library page is shown.
    /// Only touches the status, so running downloads keep their rows.
    /// </summary>
    public void RecheckFiles()
    {
        foreach (var row in this.all.Where(r => r.Status is DownloadStatus.Done or DownloadStatus.Missing))
        {
            row.Status = row.FilePath is { } file && File.Exists(paths.ToLibraryPath(file))
                ? DownloadStatus.Done
                : DownloadStatus.Missing;
        }
        this.OnTracksChanged();
    }

    private Task DownloadAllAsync(DownloadStatus status) =>
        this.DownloadTracksAsync(this.all.Where(r => r.Status == status).ToList());

    /// <summary>Downloads the given rows one by one; "Cancel downloads" stops the rest.</summary>
    public async Task DownloadTracksAsync(IReadOnlyList<TrackRowViewModel> rows)
    {
        using var cts = this.batch = new CancellationTokenSource();
        try
        {
            foreach (var row in rows)
            {
                if (cts.IsCancellationRequested)
                    break;
                await this.DownloadRowAsync(row);
            }
        }
        finally
        {
            this.batch = null;
        }
    }

    private static FileFilter CsvFilter => new(Strings.Csv_FileType, "csv");

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var path = await platform.PickSaveFileAsync(Strings.Csv_ExportTitle, Strings.Csv_DefaultFileName, CsvFilter);
        if (path is null)
            return;

        this.Notice = null;
        try
        {
            var tracks = await repository.ListDisplayAsync();
            await using (var writer = new StreamWriter(path, append: false, CsvFormat.ENCODING))
                LibraryCsv.Export(tracks, writer);
            activity.Confirm(string.Format(Strings.Confirm_Exported, tracks.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Notice = string.Format(Strings.Notice_ExportFailed, ex.Message);
        }
    }

    /// <summary>
    /// Adds the tracks of a CSV backup one by one and applies its corrections, genres and ratings.
    /// Known tracks are not fetched again; rows that fail are listed afterwards.
    /// </summary>
    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = await platform.PickOpenFileAsync(Strings.Csv_ImportTitle, CsvFilter);
        if (path is null)
            return;

        this.Notice = null;
        CsvReadResult csv;
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            csv = LibraryCsv.Read(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Notice = string.Format(Strings.Notice_ImportResult, 0, 0, ex.Message);
            return;
        }

        var errors = csv.Errors.Select(Describe).ToList();
        var imported = 0;
        if (csv.Tracks.Count > 0)
        {
            try
            {
                await toolSetup.EnsureAsync();
                imported = await this.ImportTracksAsync(csv.Tracks, errors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        await this.LoadAsync();
        if (errors.Count == 0)
        {
            activity.Confirm(string.Format(Strings.Confirm_Imported, imported));
            return;
        }

        var shown = string.Join("; ", errors.Take(3)) + (errors.Count > 3 ? " …" : string.Empty);
        this.Notice = string.Format(Strings.Notice_ImportResult, imported, errors.Count, shown);
    }

    private async Task<int> ImportTracksAsync(IReadOnlyList<CsvTrack> rows, List<string> errors)
    {
        var known = (await repository.ListDisplayAsync()).ToDictionary(t => t.VideoId, t => t.Id);
        var imported = 0;
        using var progress = activity.Begin(string.Format(Strings.Activity_Importing, 0, rows.Count));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            progress.Update(string.Format(Strings.Activity_Importing, i + 1, rows.Count));
            try
            {
                if (!known.TryGetValue(row.VideoId, out var id))
                {
                    id = await importer.AddAsync(row.Url);
                    known[row.VideoId] = id;
                }

                if (await repository.GetDisplayAsync(id) is { } current)
                {
                    var corrections = LibraryCsv.Corrections(row, current);
                    if (corrections.Fields is { } fields)
                        await repository.SetUserFieldsAsync(id, fields);
                    if (corrections.Genres is { } genres)
                        await repository.SetUserTagsAsync(id, genres);
                    if (corrections.Rating is { } rating)
                        await repository.SetRatingAsync(id, rating);
                }
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add(string.Format(Strings.Csv_ErrorLine, row.Line, ex.Message));
            }
        }

        return imported;
    }

    private static string Describe(CsvError error)
    {
        var problem = error.Problem switch
        {
            CsvProblem.MissingUrlColumn => Strings.Csv_MissingUrlColumn,
            CsvProblem.Malformed => Strings.Csv_Malformed,
            CsvProblem.InvalidUrl => string.Format(Strings.Csv_InvalidUrl, error.Value),
            CsvProblem.InvalidYear => string.Format(Strings.Csv_InvalidYear, error.Value),
            _ => string.Format(Strings.Csv_InvalidRating, error.Value),
        };
        return string.Format(Strings.Csv_ErrorLine, error.Line, problem);
    }

    Task ITrackActions.OpenOnYoutubeAsync(TrackRowViewModel row) => platform.OpenUrlAsync(row.Url);

    Task ITrackActions.DownloadAsync(TrackRowViewModel row) => this.DownloadRowAsync(row);

    Task ITrackActions.ShowInFolderAsync(TrackRowViewModel row) =>
        platform.OpenFolderAsync(row.FilePath is { } file
            ? Path.GetDirectoryName(paths.ToLibraryPath(file)) ?? paths.LibraryDir
            : paths.LibraryDir);

    void ITrackActions.OpenDetails(TrackRowViewModel row) => navigation.Open(createDetail(row, this));

    async Task ITrackActions.RefreshAsync(TrackRowViewModel row)
    {
        await this.RefreshRowAsync(row.Id);
        // writing tags may have renamed the file the .m3u8 files point to
        playlistSync.Request();
    }

    IReadOnlyList<PlaylistItemViewModel> ITrackActions.Playlists => playlists.Playlists;

    Task ITrackActions.AddToPlaylistAsync(TrackRowViewModel row, long playlistId) =>
        playlists.AddTrackAsync(playlistId, row.Id, row.Title);

    async Task ITrackActions.AddToNewPlaylistAsync(TrackRowViewModel row)
    {
        if (await playlists.CreateWithPromptAsync() is { } playlist)
            await playlists.AddTrackAsync(playlist.Id, row.Id, row.Title);
    }

    /// <summary>
    /// Adds rows for tracks created elsewhere, e.g. by a playlist import. Existing rows stay as they are,
    /// so running downloads keep their progress and other pages their shared rows.
    /// </summary>
    public async Task AddNewTracksAsync()
    {
        var existing = this.all.ToDictionary(r => r.Id);
        var rows = (await repository.ListDisplayAsync()).Select((track, index) =>
        {
            if (!existing.TryGetValue(track.Id, out var row))
                return this.NewRow(track, index + 1);
            row.Number = index + 1;
            return row;
        }).ToList();

        this.all.Clear();
        this.all.AddRange(rows);
        this.OnTracksChanged();
    }

    /// <summary>The shared row of a track, so other pages show the same status and progress.</summary>
    public TrackRowViewModel? RowFor(long trackId) => this.all.Find(r => r.Id == trackId);

    void ITrackActions.CancelDownload(TrackRowViewModel row)
    {
        if (this.running.TryGetValue(row.Id, out var cts))
            cts.Cancel();
    }

    async Task<bool> ITrackActions.RemoveAsync(TrackRowViewModel row)
    {
        var answer = await dialogs.ConfirmAsync(new ConfirmRequest(
            string.Format(Strings.Remove_Title, row.Title),
            Strings.Remove_Message,
            Strings.Remove_Confirm,
            OptionText: row.FilePath is null ? null : Strings.Remove_DeleteFile,
            OptionDefault: true,
            IsDestructive: true));
        if (answer is null)
            return false;

        this.Notice = null;
        try
        {
            await files.RemoveAsync(row.Id, deleteFile: answer.Option);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Notice = string.Format(Strings.Notice_RemoveFailed, row.Title, ex.Message);
            return false;
        }

        // removed from its own detail page: that page has nothing left to show
        if (navigation.Current is TrackDetailViewModel detail && detail.Row == row)
            navigation.Back();

        this.all.Remove(row);
        for (var i = 0; i < this.all.Count; i++)
            this.all[i].Number = i + 1;
        this.OnTracksChanged();
        activity.Confirm(string.Format(Strings.Confirm_Removed, row.Title));
        // the playlists lose the track, so their counts and .m3u8 files change
        await playlists.ChangedAsync();
        return true;
    }

    private async Task DownloadRowAsync(TrackRowViewModel row)
    {
        if (row.Status == DownloadStatus.Downloading)
            return;

        this.Notice = null;
        row.Status = DownloadStatus.Downloading;
        row.Progress = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.batch?.Token ?? CancellationToken.None);
        this.running[row.Id] = cts;
        this.IsDownloading = true;
        try
        {
            await toolSetup.EnsureAsync();
            using (activity.Begin(string.Format(Strings.Activity_Downloading, row.Title)))
                await downloader.DownloadAsync(row.Id, new Progress<double>(p => row.Progress = p), cts.Token);
            activity.Confirm(string.Format(Strings.Confirm_Downloaded, row.Title));
            playlistSync.Request();
        }
        catch (OperationCanceledException)
        {
            // cancelled on purpose; the download service already restored the status
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_DownloadFailed, row.Title, ex.Message);
        }
        finally
        {
            this.running.Remove(row.Id);
            this.IsDownloading = this.running.Count > 0;
        }

        await this.RefreshRowAsync(row.Id);
    }

    private async Task RefreshRowAsync(long id)
    {
        if (await repository.GetDisplayAsync(id) is not { } track)
            return;

        if (this.all.Find(r => r.Id == id) is { } row)
            row.Update(track, files.StatusOf(track));
        else
            this.all.Add(this.NewRow(track, this.all.Count + 1));
        this.OnTracksChanged();
    }

    private TrackRowViewModel NewRow(TrackDisplay track, int number) => new(track, files.StatusOf(track), number, this);

    private void OnTracksChanged()
    {
        this.HasMissing = this.all.Any(r => r.Status == DownloadStatus.Missing);
        this.ApplyFilter();
        this.UpdateSummary();
        this.DownloadPendingCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        var search = this.SearchText.Trim();
        var visible = this.all.Where(r => this.MatchesFilter(r.Status) && (search.Length == 0 || r.Matches(search)));

        var selected = this.SelectedTrack;
        this.Tracks.Clear();
        foreach (var row in visible)
            this.Tracks.Add(row);
        this.SelectedTrack = selected is not null && this.Tracks.Contains(selected) ? selected : null;

        this.EmptyText = this.Tracks.Count > 0 ? null
            : this.all.Count == 0 ? Strings.Library_EmptyNoTracks
            : Strings.Library_EmptyFiltered;
    }

    private bool MatchesFilter(DownloadStatus status) => this.Filter switch
    {
        TrackFilter.Done => status == DownloadStatus.Done,
        TrackFilter.Pending => status is DownloadStatus.Pending or DownloadStatus.Downloading,
        TrackFilter.Error => status is DownloadStatus.Error or DownloadStatus.Missing,
        _ => true,
    };

    private void UpdateSummary()
    {
        if (this.all.Count == 0)
        {
            this.Summary = Strings.Library_SummaryEmpty;
            return;
        }

        var done = this.all.Count(r => r.Status == DownloadStatus.Done);
        var errors = this.all.Count(r => r.Status == DownloadStatus.Error);
        var missing = this.all.Count(r => r.Status == DownloadStatus.Missing);
        var count = string.Format(this.all.Count == 1 ? Strings.Library_TrackCount_One : Strings.Library_TrackCount_Other, this.all.Count);
        var summary = string.Format(Strings.Library_Summary, count, done);
        if (errors > 0)
            summary = string.Format(Strings.Library_SummaryErrors, summary, errors);
        if (missing > 0)
            summary = string.Format(Strings.Library_SummaryMissing, summary, missing);
        this.Summary = summary;
    }
}
