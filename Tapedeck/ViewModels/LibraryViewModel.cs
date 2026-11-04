using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    ToolService tools,
    AppPaths paths,
    IPlatformService platform,
    ActivityService activity,
    NavigationService navigation,
    TrackDetailFactory createDetail) : ViewModelBase, ITrackActions
{
    private readonly List<TrackRowViewModel> all = [];
    private Task? toolsReady;

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
        this.all.Clear();
        this.all.AddRange(tracks.Select((track, index) => new TrackRowViewModel(track, index + 1, this)));
        this.OnTracksChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddLink))]
    private async Task AddLinkAsync()
    {
        this.Notice = null;
        // the field stays editable while tools and metadata load, so import the link that was confirmed
        var link = this.NewLink;
        try
        {
            await this.EnsureToolsAsync();
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

    private bool CanAddLink() => VideoId.TryParse(this.NewLink, out _);

    [RelayCommand]
    private async Task PasteLinkAsync()
    {
        if (await platform.GetClipboardTextAsync() is { } text)
            this.NewLink = text.Trim();
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private async Task DownloadPendingAsync()
    {
        foreach (var row in this.all.Where(r => r.Status == DownloadStatus.Pending).ToList())
            await this.DownloadRowAsync(row);
    }

    private bool HasPending() => this.all.Any(r => r.Status == DownloadStatus.Pending);

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
                await this.EnsureToolsAsync();
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

    Task ITrackActions.RefreshAsync(TrackRowViewModel row) => this.RefreshRowAsync(row.Id);

    private async Task DownloadRowAsync(TrackRowViewModel row)
    {
        if (row.Status == DownloadStatus.Downloading)
            return;

        this.Notice = null;
        row.Status = DownloadStatus.Downloading;
        row.Progress = 0;
        try
        {
            await this.EnsureToolsAsync();
            using (activity.Begin(string.Format(Strings.Activity_Downloading, row.Title)))
                await downloader.DownloadAsync(row.Id, new Progress<double>(p => row.Progress = p));
            activity.Confirm(string.Format(Strings.Confirm_Downloaded, row.Title));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_DownloadFailed, row.Title, ex.Message);
        }

        await this.RefreshRowAsync(row.Id);
    }

    /// <summary>yt-dlp, ffmpeg and Deno are downloaded once on first use.</summary>
    private async Task EnsureToolsAsync()
    {
        try
        {
            if (tools.HasAllTools)
                return;
            using (activity.Begin(Strings.Activity_InstallingTools))
                await (this.toolsReady ??= tools.EnsureToolsAsync());
        }
        catch
        {
            this.toolsReady = null;
            throw;
        }
    }

    private async Task RefreshRowAsync(long id)
    {
        if (await repository.GetDisplayAsync(id) is not { } track)
            return;

        if (this.all.Find(r => r.Id == id) is { } row)
            row.Update(track);
        else
            this.all.Add(new TrackRowViewModel(track, this.all.Count + 1, this));
        this.OnTracksChanged();
    }

    private void OnTracksChanged()
    {
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
        TrackFilter.Error => status == DownloadStatus.Error,
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
        var count = string.Format(this.all.Count == 1 ? Strings.Library_TrackCount_One : Strings.Library_TrackCount_Other, this.all.Count);
        var summary = string.Format(Strings.Library_Summary, count, done);
        this.Summary = errors > 0 ? string.Format(Strings.Library_SummaryErrors, summary, errors) : summary;
    }
}
