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
using Tapedeck.Core.Settings;
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
    /// <summary>MusicBrainz found candidates the user should confirm.</summary>
    Suggested,
    /// <summary>The same song from several videos, shown next to each other.</summary>
    Duplicates,
}

/// <summary>Library page: add links, list, filter and download tracks.</summary>
public partial class LibraryViewModel(
    ITrackRepository repository,
    TrackImportService importer,
    CsvImportService csvImporter,
    DownloadService downloader,
    ToolSetup toolSetup,
    AppPaths paths,
    IPlatformService platform,
    ActivityService activity,
    NavigationService navigation,
    LibraryFiles files,
    ExistingFileFinder fileFinder,
    DialogService dialogs,
    PlaylistsViewModel playlists,
    PlaylistSync playlistSync,
    MusicBrainzQueue musicBrainz,
    TagWriter tagWriter,
    PlayerService player,
    LoudnessQueue loudness,
    CoverQueue covers,
    LastFmQueue lastFm,
    SettingsStore settings,
    TrackDetailFactory createDetail) : ViewModelBase, ITrackActions
{
    // a search plus up to three genre lookups at one request per second, with the occasional retry
    private const double MATCH_SECONDS_PER_TRACK = 4;

    // this many failed downloads in a row mean YouTube refuses for now; trying the rest would only fail too
    private const int MAX_DOWNLOAD_FAILURES_IN_A_ROW = 5;

    private readonly List<TrackRowViewModel> all = [];
    // one per running download, so a single row can be cancelled
    private readonly Dictionary<long, CancellationTokenSource> running = [];
    // the current "download pending" or "download missing" run
    private CancellationTokenSource? batch;
    // each duplicate's group (all its copies, itself included), and the group's place for sorting
    private Dictionary<long, (IReadOnlyList<long> Group, int Index)> duplicates = [];
    private bool listening;

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

    /// <summary>The MusicBrainz run is going; the toolbar offers to stop it.</summary>
    [ObservableProperty]
    public partial bool IsMatching { get; private set; }

    /// <summary>Downloaded tracks whose file tags are older than the shown values; shows the bulk action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaleTags), nameof(WriteAllTagsText))]
    [NotifyCanExecuteChangedFor(nameof(WriteAllTagsCommand))]
    public partial int StaleTagsCount { get; private set; }

    public bool HasStaleTags => this.StaleTagsCount > 0;

    public string WriteAllTagsText => string.Format(Strings.Library_WriteAllTags, this.StaleTagsCount);

    /// <summary>Some tracks wait for the user to confirm a MusicBrainz suggestion; shows the filter chip.</summary>
    [ObservableProperty]
    public partial bool HasSuggestions { get; private set; }

    /// <summary>Some songs are in the library more than once; shows the filter chip.</summary>
    [ObservableProperty]
    public partial bool HasDuplicates { get; private set; }

    partial void OnFilterChanged(TrackFilter value) => this.ApplyFilter();

    partial void OnSearchTextChanged(string value) => this.ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        this.Listen();
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
            {
                id = await importer.AddAsync(link);
                // the file may already be in the library folder, e.g. copied from another computer
                await fileFinder.AdoptAsync([id]);
            }
            if (this.NewLink == link)
                this.NewLink = string.Empty;
            await this.RefreshRowAsync(id);
            if (this.all.Find(r => r.Id == id) is { } row)
                activity.Confirm(string.Format(Strings.Confirm_Added, row.Title));
            musicBrainz.EnqueueImported([id]);
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
    /// Looks up every track on MusicBrainz again, one after another in the background. Matches stay when nothing
    /// certain is found, so their genres and covers get refreshed; tracks the user declined are left alone.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMatchable))]
    private async Task MatchMusicBrainzAsync()
    {
        var ids = this.all.Where(r => r.MbState != MbMatchState.Rejected).Select(r => r.Id).ToList();
        var minutes = Math.Max(1, (int)Math.Ceiling(ids.Count * MATCH_SECONDS_PER_TRACK / 60));
        var answer = await dialogs.ConfirmAsync(new ConfirmRequest(
            Strings.MatchAll_Title, string.Format(Strings.MatchAll_Message, ids.Count, minutes), Strings.MatchAll_Confirm));
        if (answer is null)
            return;

        this.Notice = null;
        musicBrainz.Enqueue(ids, all: true);
    }

    private bool HasMatchable() => this.all.Any(r => r.MbState != MbMatchState.Rejected);

    [RelayCommand]
    private void CancelMatching() => musicBrainz.Cancel();

    /// <summary>
    /// Writes the outdated tags of all downloaded tracks, one after another, and renames their files to match,
    /// as "Write tags" on the detail page does. A file that fails does not stop the others.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasStaleTags))]
    private async Task WriteAllTagsAsync()
    {
        var rows = this.all.Where(IsTagWritable).ToList();
        this.Notice = null;
        var written = 0;
        var renamed = 0;
        var failed = new List<string>();

        using (var progress = activity.Begin(string.Format(Strings.Activity_WritingAllTags, 0, rows.Count)))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                progress.Update(string.Format(Strings.Activity_WritingAllTags, i + 1, rows.Count));
                // a download may have started meanwhile; it writes its own file
                if (row.IsDownloading)
                    continue;
                try
                {
                    // a playing file is open, so it could neither be renamed nor written
                    await player.StopIfLoadedAsync(row.Id);
                    // the file name follows corrected artist and title, then the tags go in
                    if (await files.RenameToMatchAsync(row.Id) is not null)
                        renamed++;
                    await tagWriter.WriteTagsAsync(row.Id);
                    written++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Title}: {ex.Message}");
                }

                // update the row quietly; the table is rebuilt once at the end
                if (await repository.GetDisplayAsync(row.Id) is { } track)
                    row.Update(track, files.StatusOf(track));
            }
        }

        this.OnTracksChanged();
        if (renamed > 0)
            playlistSync.Request();

        if (failed.Count == 0)
        {
            activity.Confirm(string.Format(Strings.Confirm_AllTagsWritten, written, renamed));
            return;
        }
        var shown = string.Join("; ", failed.Take(3)) + (failed.Count > 3 ? " …" : string.Empty);
        this.Notice = string.Format(Strings.Notice_WriteAllTagsFailed, written, failed.Count, shown);
    }

    /// <summary>Only files that are there can take tags; a missing file has to be downloaded first.</summary>
    private static bool IsTagWritable(TrackRowViewModel row) => row.TagsStale && row.Status == DownloadStatus.Done;

    /// <summary>Wires the MusicBrainz run and the player to the rows once, on the first load.</summary>
    private void Listen()
    {
        if (this.listening)
            return;
        this.listening = true;

        player.Failed += message => this.Notice = message;
        player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlayerService.CurrentTrackId) or nameof(PlayerService.IsPlaying))
                this.MarkPlaying();
        };

        musicBrainz.TrackChanged += id => _ = this.OnMatchedAsync(id);
        loudness.Measured += ids => _ = this.RefreshRowsQuietlyAsync(ids);
        covers.Covered += ids => _ = this.RefreshRowsQuietlyAsync(ids);
        lastFm.Tagged += ids => _ = this.RefreshRowsQuietlyAsync(ids);
        lastFm.Failed += message => this.Notice = message;
        musicBrainz.Failed += message => this.Notice = string.Format(Strings.Notice_MusicBrainzFailed, message);
        musicBrainz.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MusicBrainzQueue.IsRunning))
                return;
            this.IsMatching = musicBrainz.IsRunning;
            // Last.fm only fills in what MusicBrainz did not find
            if (!musicBrainz.IsRunning)
                lastFm.EnqueueMissing();
        };
    }

    private async Task OnMatchedAsync(long id)
    {
        // the row is shared with the playlist pages, so they show the new values too
        await this.RefreshRowAsync(id);
        if (navigation.Current is TrackDetailViewModel detail && detail.Row.Id == id)
            await detail.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// New loudness values and covers from the background make the tags outdated; the rows learn it quietly,
    /// the table updates once.
    /// </summary>
    private async Task RefreshRowsQuietlyAsync(IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
        {
            if (this.all.Find(r => r.Id == id) is { } row && await repository.GetDisplayAsync(id) is { } track)
                row.Update(track, files.StatusOf(track));
        }
        this.OnTracksChanged();
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

    /// <summary>
    /// Downloads the given rows, a few at once (see <see cref="AppSettings.ParallelDownloads"/>), so a long list is done
    /// sooner: YouTube slows down each connection, and every download also waits for yt-dlp to start and read the page.
    /// One toast counts the whole batch; "Cancel downloads" stops the rest. Several failures in a row stop the batch,
    /// since then YouTube is most likely refusing.
    /// </summary>
    public async Task DownloadTracksAsync(IReadOnlyList<TrackRowViewModel> rows)
    {
        rows = (await this.TakeOverFilesAsync(rows)).Where(r => r.Status != DownloadStatus.Downloading).ToList();
        if (rows.Count <= 1)
        {
            foreach (var row in rows)
                await this.DownloadRowAsync(row);
            return;
        }

        var parallel = Math.Clamp(settings.Load().ParallelDownloads ?? AppSettings.DEFAULT_PARALLEL_DOWNLOADS, 1, AppSettings.MAX_PARALLEL_DOWNLOADS);
        int downloaded = 0, failed = 0, failuresInARow = 0;
        (string Title, string Error)? firstFailure = null;
        var stopped = false;
        using var cts = this.batch = new CancellationTokenSource();
        try
        {
            using var progress = activity.Begin(string.Format(Strings.Activity_DownloadingMany, 0, rows.Count, parallel));
            await Throttle.ForEachAsync(rows, parallel, async row =>
            {
                var error = await this.DownloadRowAsync(row, quiet: true);
                if (error is null)
                {
                    downloaded++;
                    failuresInARow = 0;
                }
                else if (error.Length > 0)
                {
                    failed++;
                    firstFailure ??= (row.Title, error);
                    if (++failuresInARow >= MAX_DOWNLOAD_FAILURES_IN_A_ROW && !cts.IsCancellationRequested)
                    {
                        stopped = true;
                        cts.Cancel();
                    }
                }
                progress.Update(string.Format(Strings.Activity_DownloadingMany, downloaded + failed, rows.Count, parallel));
            }, cts.Token);
        }
        finally
        {
            this.batch = null;
        }

        if (stopped && firstFailure is { } cause)
            this.Notice = string.Format(Strings.Notice_DownloadsStopped, MAX_DOWNLOAD_FAILURES_IN_A_ROW, cause.Error);
        else if (firstFailure is { } first)
            this.Notice = string.Format(Strings.Notice_DownloadsFailed, failed, first.Title, first.Error);
        if (downloaded > 0)
            activity.Confirm(downloaded == 1 ? Strings.Confirm_DownloadedMany_One : string.Format(Strings.Confirm_DownloadedMany_Other, downloaded));
    }

    public static FileFilter CsvFilter => new(Strings.Csv_FileType, "csv");

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        this.Notice = await this.SaveCsvAsync(repository.ListDisplayAsync, Strings.Csv_DefaultFileName);
    }

    /// <summary>
    /// Asks where to save and writes the tracks as CSV, in the given order; used for the whole library and for a playlist.
    /// </summary>
    /// <param name="loadTracks">Loaded only once the file was chosen, so the export has the latest values.</param>
    /// <returns>Why the file could not be written, or <c>null</c>.</returns>
    public async Task<string?> SaveCsvAsync(Func<Task<IReadOnlyList<TrackDisplay>>> loadTracks, string defaultFileName)
    {
        var path = await platform.PickSaveFileAsync(Strings.Csv_ExportTitle, defaultFileName, CsvFilter);
        if (path is null)
            return null;

        try
        {
            var tracks = await loadTracks();
            await using (var writer = new StreamWriter(path, append: false, CsvFormat.ENCODING))
                LibraryCsv.Export(tracks, writer);
            activity.Confirm(string.Format(Strings.Confirm_Exported, tracks.Count));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Format(Strings.Notice_ExportFailed, ex.Message);
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
        IReadOnlyList<long> imported = [];
        var found = 0;
        if (csv.Tracks.Count > 0)
        {
            try
            {
                await toolSetup.EnsureAsync();
                imported = await this.ImportTracksAsync(csv.Tracks, errors);
                found = (await fileFinder.AdoptAsync()).Count;
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        await this.LoadAsync();
        musicBrainz.EnqueueImported(imported);
        if (found > 0)
            playlistSync.Request();
        if (errors.Count == 0)
        {
            activity.Confirm(found > 0
                ? string.Format(Strings.Confirm_ImportedWithFiles, imported.Count, found)
                : string.Format(Strings.Confirm_Imported, imported.Count));
            return;
        }

        var shown = string.Join("; ", errors.Take(3)) + (errors.Count > 3 ? " …" : string.Empty);
        this.Notice = string.Format(Strings.Notice_ImportResult, imported.Count, errors.Count, shown);
    }

    /// <returns>The imported tracks.</returns>
    private async Task<IReadOnlyList<long>> ImportTracksAsync(IReadOnlyList<CsvTrack> rows, List<string> errors)
    {
        CsvImportResult result;
        using (var progress = activity.Begin(string.Format(Strings.Activity_Importing, 0, rows.Count)))
            result = await csvImporter.ImportAsync(rows, new Progress<int>(done =>
                progress.Update(string.Format(Strings.Activity_Importing, done, rows.Count))));
        errors.AddRange(result.Errors.Select(Describe));
        return result.TrackIds;
    }

    /// <summary>A failed CSV row for the notice, with its line number.</summary>
    public static string Describe(CsvImportError error) => string.Format(Strings.Csv_ErrorLine, error.Line, error.Message);

    /// <summary>A CSV row that could not be read, with its line number.</summary>
    public static string Describe(CsvError error)
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

    async Task ITrackActions.TogglePlayAsync(TrackRowViewModel row)
    {
        if (!row.CanPlay)
            return;
        this.Notice = null;
        // without leveling the file plays as it is
        var loudness = settings.Load().LevelPlayback ?? true ? await repository.GetLoudnessAsync(row.Id) : null;
        await player.ToggleAsync(new PlayRequest(row.Id, paths.ToLibraryPath(row.FilePath!), row.Title, row.Artist,
            row.CoverPath, TimeSpan.FromSeconds(row.DurationS ?? 0), loudness));
    }

    /// <summary>Shows on every row whether it is the loaded track and whether it plays.</summary>
    private void MarkPlaying()
    {
        foreach (var row in this.all)
        {
            var loaded = player.CurrentTrackId == row.Id;
            row.IsLoaded = loaded;
            row.IsPlaying = loaded && player.IsPlaying;
        }
    }

    async Task ITrackActions.DownloadAsync(TrackRowViewModel row)
    {
        if ((await this.TakeOverFilesAsync([row])).Count > 0)
            await this.DownloadRowAsync(row);
    }

    Task ITrackActions.ShowInFolderAsync(TrackRowViewModel row) =>
        platform.OpenFolderAsync(row.FilePath is { } file
            ? Path.GetDirectoryName(paths.ToLibraryPath(file)) ?? paths.LibraryDir
            : paths.LibraryDir);

    /// <summary>
    /// Opens the details with the list they were opened from: a playlist in its order, else the library as filtered
    /// and searched right now. The arrows on the detail page go through that list.
    /// </summary>
    void ITrackActions.OpenDetails(TrackRowViewModel row)
    {
        var sequence = navigation.Current is PlaylistDetailViewModel playlist
            ? new TrackSequence(playlist.Entries.Select(e => e.Row).ToList(), playlist.SelectRow)
            : new TrackSequence(this.Tracks.ToList(), r => this.SelectedTrack = r);
        sequence.Select(row);
        navigation.Open(createDetail(row, this, sequence));
    }

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
        var newIds = (await repository.ListDisplayAsync()).Select(t => t.Id).Where(id => !existing.ContainsKey(id)).ToList();
        if (newIds.Count > 0)
        {
            await fileFinder.AdoptAsync(newIds);
            musicBrainz.EnqueueImported(newIds);
        }

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

    /// <summary>
    /// Takes over existing files and shows the current values of these tracks, e.g. after a playlist's CSV import
    /// changed corrections of known tracks. New tracks get rows, existing rows stay shared.
    /// </summary>
    public async Task RefreshTracksAsync(IReadOnlyList<long> trackIds)
    {
        try
        {
            await fileFinder.AdoptAsync(trackIds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the folder cannot be read; the tracks can still be downloaded
        }
        await this.AddNewTracksAsync();
        foreach (var id in trackIds.Distinct())
            await this.RefreshRowAsync(id);
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
            await player.StopIfLoadedAsync(row.Id);
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

    async Task ITrackActions.KeepAsync(TrackRowViewModel row)
    {
        if (!this.duplicates.TryGetValue(row.Id, out var duplicate))
            return;
        var others = this.all.Where(r => r != row && duplicate.Group.Contains(r.Id)).ToList();
        if (others.Any(r => r.IsDownloading))
        {
            this.Notice = string.Format(Strings.Notice_KeepWhileDownloading, row.Title);
            return;
        }

        // "don't ask again" remembers the file choice along with it
        var answer = settings.Load().KeepDeletesFiles is { } remembered
            ? new ConfirmResult(remembered)
            : await dialogs.ConfirmAsync(new ConfirmRequest(
                string.Format(Strings.Keep_Title, row.Title),
                others.Count == 1 ? Strings.Keep_Message_One : string.Format(Strings.Keep_Message_Other, others.Count),
                Strings.Keep_Confirm,
                // shown even without files, so a remembered answer always says what the user chose
                OptionText: Strings.Keep_DeleteFiles,
                OptionDefault: true,
                IsDestructive: true,
                OfferDontAsk: true));
        if (answer is null)
            return;
        if (answer.DontAskAgain)
            settings.Update(s => s with { KeepDeletesFiles = answer.Option });

        this.Notice = null;
        try
        {
            foreach (var other in others)
                await player.StopIfLoadedAsync(other.Id);
            await files.KeepAsync(row.Id, duplicate.Group, deleteFiles: answer.Option);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Notice = string.Format(Strings.Notice_KeepFailed, row.Title, ex.Message);
            // some copies may be gone already
            await this.LoadAsync();
            return;
        }

        // a removed copy's detail page has nothing left to show
        if (navigation.Current is TrackDetailViewModel detail && others.Contains(detail.Row))
            navigation.Back();

        foreach (var other in others)
            this.all.Remove(other);
        for (var i = 0; i < this.all.Count; i++)
            this.all[i].Number = i + 1;
        // the kept track may have taken over a rating
        if (await repository.GetDisplayAsync(row.Id) is { } track)
            row.Update(track, files.StatusOf(track));
        this.OnTracksChanged();
        activity.Confirm(string.Format(Strings.Confirm_Kept, row.Title, others.Count));
        // the playlists got the kept track in place of the copies
        await playlists.ChangedAsync();
    }

    /// <summary>Marks the rows whose song is in the library more than once.</summary>
    private void FindDuplicates()
    {
        var groups = DuplicateFinder.Groups(this.all.Select(r => new DuplicateFinder.Song(r.Id, r.Artist, r.Title, r.MbRecordingId)));
        this.duplicates = groups
            .SelectMany((group, index) => group.Select(id => (id, group, index)))
            .ToDictionary(e => e.id, e => (e.group, e.index));
        foreach (var row in this.all)
            row.IsDuplicate = this.duplicates.ContainsKey(row.Id);
        // the chip stays while it is selected, so the user sees the list run empty
        this.HasDuplicates = this.Filter == TrackFilter.Duplicates || this.duplicates.Count > 0;
    }

    /// <summary>
    /// Gives rows without a file the matching file from the library folder instead of downloading it twice.
    /// </summary>
    /// <returns>The rows that still need a download.</returns>
    private async Task<IReadOnlyList<TrackRowViewModel>> TakeOverFilesAsync(IReadOnlyList<TrackRowViewModel> rows)
    {
        IReadOnlyList<long> adopted;
        try
        {
            adopted = await fileFinder.AdoptAsync(rows.Select(r => r.Id).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the folder cannot be read; downloading still works
            return rows;
        }
        if (adopted.Count == 0)
            return rows;

        foreach (var id in adopted)
            await this.RefreshRowAsync(id);
        playlistSync.Request();
        activity.Confirm(adopted.Count == 1
            ? Strings.Confirm_FilesFound_One
            : string.Format(Strings.Confirm_FilesFound_Other, adopted.Count));
        return rows.Where(r => !adopted.Contains(r.Id)).ToList();
    }

    /// <param name="quiet">Part of a batch: no toast and notice of its own, the batch reports for all.</param>
    /// <returns><c>null</c> when downloaded, empty when cancelled or skipped, otherwise the error.</returns>
    private async Task<string?> DownloadRowAsync(TrackRowViewModel row, bool quiet = false)
    {
        if (row.Status == DownloadStatus.Downloading)
            return string.Empty;

        if (!quiet)
            this.Notice = null;
        // the new download replaces the file that may be playing
        await player.StopIfLoadedAsync(row.Id);
        row.Status = DownloadStatus.Downloading;
        row.Progress = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.batch?.Token ?? CancellationToken.None);
        this.running[row.Id] = cts;
        this.IsDownloading = true;
        string? error = null;
        try
        {
            await toolSetup.EnsureAsync();
            using (quiet ? null : activity.Begin(string.Format(Strings.Activity_Downloading, row.Title)))
                await downloader.DownloadAsync(row.Id, new Progress<double>(p => row.Progress = p), cts.Token);
            if (!quiet)
                activity.Confirm(string.Format(Strings.Confirm_Downloaded, row.Title));
            playlistSync.Request();
        }
        catch (OperationCanceledException)
        {
            // cancelled on purpose; the download service already restored the status
            error = string.Empty;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (!quiet)
                this.Notice = string.Format(Strings.Notice_DownloadFailed, row.Title, ex.Message);
        }
        finally
        {
            this.running.Remove(row.Id);
            this.IsDownloading = this.running.Count > 0;
        }

        await this.RefreshRowAsync(row.Id);
        return error;
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
        this.StaleTagsCount = this.all.Count(IsTagWritable);
        // rows built anew (reload, import) learn whether they are the playing track
        this.MarkPlaying();
        // the chip stays while it is selected, so the user sees the list run empty
        this.HasSuggestions = this.Filter == TrackFilter.Suggested || this.all.Any(r => r.MbState == MbMatchState.Suggested);
        this.FindDuplicates();
        this.ApplyFilter();
        this.UpdateSummary();
        this.DownloadPendingCommand.NotifyCanExecuteChanged();
        this.MatchMusicBrainzCommand.NotifyCanExecuteChanged();
        // smart playlists pick their tracks from the shown values, so their .m3u8 may change with any track
        playlistSync.Request();
        // downloads measure themselves; older and adopted files get their value in the background
        loudness.EnqueueMissing();
        // tracks without a Cover Art Archive cover get one cut from their thumbnail
        covers.EnqueueMissing();
        // tracks without genres ask Last.fm, with the user's key
        lastFm.EnqueueMissing();
    }

    private void ApplyFilter()
    {
        var search = this.SearchText.Trim();
        var visible = this.all.Where(r => this.MatchesFilter(r) && (search.Length == 0 || r.Matches(search)));
        // the copies of a song next to each other, so the user can compare them
        if (this.Filter == TrackFilter.Duplicates)
            visible = visible.OrderBy(r => this.duplicates[r.Id].Index).ThenBy(r => r.Number);

        var selected = this.SelectedTrack;
        this.Tracks.Clear();
        foreach (var row in visible)
            this.Tracks.Add(row);
        this.SelectedTrack = selected is not null && this.Tracks.Contains(selected) ? selected : null;

        this.EmptyText = this.Tracks.Count > 0 ? null
            : this.all.Count == 0 ? Strings.Library_EmptyNoTracks
            : Strings.Library_EmptyFiltered;
    }

    private bool MatchesFilter(TrackRowViewModel row) => this.Filter switch
    {
        TrackFilter.Done => row.Status == DownloadStatus.Done,
        TrackFilter.Pending => row.Status is DownloadStatus.Pending or DownloadStatus.Downloading,
        TrackFilter.Error => row.Status is DownloadStatus.Error or DownloadStatus.Missing,
        TrackFilter.Suggested => row.MbState == MbMatchState.Suggested,
        TrackFilter.Duplicates => this.duplicates.ContainsKey(row.Id),
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
