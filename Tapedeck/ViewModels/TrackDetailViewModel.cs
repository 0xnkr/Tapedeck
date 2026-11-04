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
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Creates the detail page for a library row; the row's page carries out downloads and file actions.</summary>
public delegate TrackDetailViewModel TrackDetailFactory(TrackRowViewModel row, ITrackActions actions);

/// <summary>
/// Detail page of a track: corrections are saved shortly after typing stops,
/// "write tags" puts them into the audio file.
/// </summary>
public partial class TrackDetailViewModel : ViewModelBase
{
    private static readonly TimeSpan SAVE_DELAY = TimeSpan.FromMilliseconds(600);

    private readonly ITrackActions actions;
    private readonly ITrackRepository repository;
    private readonly TagWriter tagWriter;
    private readonly ActivityService activity;
    private readonly NavigationService navigation;

    private CancellationTokenSource? pendingSave;
    private string? notes;
    private bool loading;

    public TrackDetailViewModel(TrackRowViewModel row, ITrackActions actions, ITrackRepository repository,
        TagWriter tagWriter, ActivityService activity, NavigationService navigation)
    {
        this.Row = row;
        this.actions = actions;
        this.repository = repository;
        this.tagWriter = tagWriter;
        this.activity = activity;
        this.navigation = navigation;

        this.ArtistField = new MetadataFieldViewModel(Strings.Field_Artist, this.ScheduleSave);
        this.TitleField = new MetadataFieldViewModel(Strings.Field_Title, this.ScheduleSave);
        this.AlbumField = new MetadataFieldViewModel(Strings.Field_Album, this.ScheduleSave);
        this.AlbumArtistField = new MetadataFieldViewModel(Strings.Field_AlbumArtist, this.ScheduleSave);
        this.YearField = new MetadataFieldViewModel(Strings.Field_Year, this.ScheduleSave);
        this.Fields = [this.ArtistField, this.TitleField, this.AlbumField, this.AlbumArtistField, this.YearField];
    }

    /// <summary>The library row: status and progress stay in sync with downloads started anywhere.</summary>
    public TrackRowViewModel Row { get; }

    public MetadataFieldViewModel ArtistField { get; }
    public MetadataFieldViewModel TitleField { get; }
    public MetadataFieldViewModel AlbumField { get; }
    public MetadataFieldViewModel AlbumArtistField { get; }
    public MetadataFieldViewModel YearField { get; }
    public IReadOnlyList<MetadataFieldViewModel> Fields { get; }

    public ObservableCollection<string> Genres { get; } = [];

    [ObservableProperty]
    public partial FieldSource GenresSource { get; private set; }

    [ObservableProperty]
    public partial bool IsAddingGenre { get; set; }

    [ObservableProperty]
    public partial string NewGenre { get; set; } = "";

    [ObservableProperty]
    public partial int? Rating { get; set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    /// <summary>"Artist · Album · 3:52", missing parts left out.</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    [ObservableProperty]
    public partial string? ThumbPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyCanExecuteChangedFor(nameof(WriteTagsCommand))]
    public partial string? FileName { get; private set; }

    public bool HasFile => this.FileName is not null;

    [ObservableProperty]
    public partial string? FileFormat { get; private set; }

    [ObservableProperty]
    public partial string? DownloadedAt { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial bool TagsStale { get; private set; }

    [ObservableProperty]
    public partial string? Channel { get; private set; }

    [ObservableProperty]
    public partial string? UploadedAt { get; private set; }

    [ObservableProperty]
    public partial string VideoId { get; private set; } = "";

    [ObservableProperty]
    public partial string? AddedAt { get; private set; }

    /// <summary>Last error, shown below the header.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (await this.repository.GetDisplayAsync(this.Row.Id) is not { } track)
            return;

        this.loading = true;
        this.Title = track.Title;
        this.Subtitle = string.Join(" · ", new[] { track.Artist, track.Album, Row.DurationText }.Where(p => !string.IsNullOrEmpty(p)));
        this.ThumbPath = track.ThumbPath;
        this.FileName = track.FilePath;
        this.FileFormat = track.FileFormat;
        this.DownloadedAt = FormatTimestamp(track.DownloadedAt);
        this.ErrorMessage = track.ErrorMsg;
        this.TagsStale = track.TagsStale;
        this.Channel = track.Channel;
        this.UploadedAt = FormatUploadDate(track.UploadDate);
        this.VideoId = track.VideoId;
        this.AddedAt = FormatTimestamp(track.AddedAt);
        this.Rating = track.Rating;
        this.notes = track.Notes;

        this.Genres.Clear();
        foreach (var genre in track.GenreList)
            this.Genres.Add(genre);
        this.GenresSource = track.GenresFrom;

        // fields the user is typing in right now keep their text
        this.LoadField(this.ArtistField, track.Artist, track.ArtistFrom);
        this.LoadField(this.TitleField, track.Title, track.TitleFrom);
        this.LoadField(this.AlbumField, track.Album, track.AlbumFrom);
        this.LoadField(this.AlbumArtistField, track.AlbumArtist, track.AlbumArtistFrom);
        this.LoadField(this.YearField, track.Year?.ToString(CultureInfo.InvariantCulture), track.YearFrom);
        this.loading = false;
    }

    [RelayCommand]
    private void Back() => this.navigation.Back();

    [RelayCommand]
    private Task OpenOnYoutubeAsync() => this.actions.OpenOnYoutubeAsync(this.Row);

    [RelayCommand]
    private Task ShowInFolderAsync() => this.actions.ShowInFolderAsync(this.Row);

    [RelayCommand]
    private async Task RedownloadAsync()
    {
        await this.SaveNowAsync();
        await this.actions.DownloadAsync(this.Row);
        await this.LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task WriteTagsAsync()
    {
        await this.SaveNowAsync();
        this.Notice = null;
        try
        {
            using (this.activity.Begin(Strings.Activity_WritingTags))
                await this.tagWriter.WriteTagsAsync(this.Row.Id);
            this.activity.Confirm(string.Format(Strings.Confirm_TagsWritten, this.Title));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_WriteTagsFailed, ex.Message);
        }

        await this.LoadAsync();
    }

    [RelayCommand]
    private void StartAddGenre()
    {
        this.NewGenre = string.Empty;
        this.IsAddingGenre = true;
    }

    [RelayCommand]
    private void CancelAddGenre() => this.IsAddingGenre = false;

    [RelayCommand]
    private async Task AddGenreAsync()
    {
        var genre = this.NewGenre.Trim();
        this.IsAddingGenre = false;
        if (genre.Length == 0 || this.Genres.Contains(genre, StringComparer.CurrentCultureIgnoreCase))
            return;
        await this.SaveGenresAsync([.. this.Genres, genre]);
    }

    [RelayCommand]
    private Task RemoveGenreAsync(string genre) =>
        this.SaveGenresAsync(this.Genres.Where(g => g != genre).ToList());

    partial void OnRatingChanged(int? value)
    {
        if (!loading)
            _ = SaveRatingAsync(value);
    }

    private async Task SaveRatingAsync(int? rating)
    {
        await this.RunSaveAsync(() => this.repository.SetRatingAsync(this.Row.Id, rating));
    }

    /// <summary>
    /// Editing any genre turns the shown list into the user's own. An empty list removes the user's genres,
    /// so the next best source shows again.
    /// </summary>
    private Task SaveGenresAsync(IReadOnlyList<string> genres) =>
        this.RunSaveAsync(() => this.repository.SetUserTagsAsync(this.Row.Id, genres));

    private void ScheduleSave()
    {
        if (this.loading)
            return;

        pendingSave?.Cancel();
        var cts = this.pendingSave = new CancellationTokenSource();
        _ = this.SaveAfterDelayAsync(cts);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SAVE_DELAY, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (this.pendingSave == cts)
                this.pendingSave = null;
            cts.Dispose();
        }

        await this.SaveFieldsAsync();
    }

    /// <summary>Saves edits still waiting for the typing pause before an action that relies on them.</summary>
    private async Task SaveNowAsync()
    {
        if (this.pendingSave is null)
            return;
        this.pendingSave.Cancel();
        this.pendingSave = null;
        await this.SaveFieldsAsync();
    }

    private async Task SaveFieldsAsync()
    {
        int? year = null;
        if (this.YearField.UserValue is { } yearText)
        {
            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1000 or > 9999)
            {
                this.Notice = string.Format(Strings.Detail_InvalidYear, yearText);
                return;
            }
            year = parsed;
        }

        var fields = new UserFields(this.ArtistField.UserValue, this.TitleField.UserValue, this.AlbumField.UserValue,
            this.AlbumArtistField.UserValue, year, this.notes);
        await this.RunSaveAsync(() => this.repository.SetUserFieldsAsync(this.Row.Id, fields));
    }

    private async Task RunSaveAsync(Func<Task> save)
    {
        this.Notice = null;
        try
        {
            await save();
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_SaveFailed, ex.Message);
            return;
        }

        await this.LoadAsync();
        await this.actions.RefreshAsync(this.Row);
    }

    private void LoadField(MetadataFieldViewModel field, string? value, FieldSource source)
    {
        // text typed while the last save ran is saved after the next pause; don't overwrite it
        if (this.pendingSave is not null && field.Text != (value ?? string.Empty))
            return;
        field.Load(value, source);
    }

    private static string? FormatTimestamp(string? isoUtc) =>
        DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : null;

    private static string? FormatUploadDate(string? yyyymmdd) =>
        DateOnly.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : null;
}
