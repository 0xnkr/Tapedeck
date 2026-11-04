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
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly ITrackActions _actions;
    private readonly ITrackRepository _repository;
    private readonly TagWriter _tagWriter;
    private readonly ActivityService _activity;
    private readonly NavigationService _navigation;

    private CancellationTokenSource? _pendingSave;
    private string? _notes;
    private bool _loading;

    public TrackDetailViewModel(TrackRowViewModel row, ITrackActions actions, ITrackRepository repository,
        TagWriter tagWriter, ActivityService activity, NavigationService navigation)
    {
        Row = row;
        _actions = actions;
        _repository = repository;
        _tagWriter = tagWriter;
        _activity = activity;
        _navigation = navigation;

        ArtistField = new MetadataFieldViewModel(Strings.Field_Artist, ScheduleSave);
        TitleField = new MetadataFieldViewModel(Strings.Field_Title, ScheduleSave);
        AlbumField = new MetadataFieldViewModel(Strings.Field_Album, ScheduleSave);
        AlbumArtistField = new MetadataFieldViewModel(Strings.Field_AlbumArtist, ScheduleSave);
        YearField = new MetadataFieldViewModel(Strings.Field_Year, ScheduleSave);
        Fields = [ArtistField, TitleField, AlbumField, AlbumArtistField, YearField];
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

    public bool HasFile => FileName is not null;

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
        if (await _repository.GetDisplayAsync(Row.Id) is not { } track)
            return;

        _loading = true;
        Title = track.Title;
        Subtitle = string.Join(" · ", new[] { track.Artist, track.Album, Row.DurationText }.Where(p => !string.IsNullOrEmpty(p)));
        ThumbPath = track.ThumbPath;
        FileName = track.FilePath;
        FileFormat = track.FileFormat;
        DownloadedAt = FormatTimestamp(track.DownloadedAt);
        ErrorMessage = track.ErrorMsg;
        TagsStale = track.TagsStale;
        Channel = track.Channel;
        UploadedAt = FormatUploadDate(track.UploadDate);
        VideoId = track.VideoId;
        AddedAt = FormatTimestamp(track.AddedAt);
        Rating = track.Rating;
        _notes = track.Notes;

        Genres.Clear();
        foreach (var genre in track.GenreList)
            Genres.Add(genre);
        GenresSource = track.GenresFrom;

        // fields the user is typing in right now keep their text
        LoadField(ArtistField, track.Artist, track.ArtistFrom);
        LoadField(TitleField, track.Title, track.TitleFrom);
        LoadField(AlbumField, track.Album, track.AlbumFrom);
        LoadField(AlbumArtistField, track.AlbumArtist, track.AlbumArtistFrom);
        LoadField(YearField, track.Year?.ToString(CultureInfo.InvariantCulture), track.YearFrom);
        _loading = false;
    }

    [RelayCommand]
    private void Back() => _navigation.Back();

    [RelayCommand]
    private Task OpenOnYoutubeAsync() => _actions.OpenOnYoutubeAsync(Row);

    [RelayCommand]
    private Task ShowInFolderAsync() => _actions.ShowInFolderAsync(Row);

    [RelayCommand]
    private async Task RedownloadAsync()
    {
        await SaveNowAsync();
        await _actions.DownloadAsync(Row);
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task WriteTagsAsync()
    {
        await SaveNowAsync();
        Notice = null;
        try
        {
            using (_activity.Begin(Strings.Activity_WritingTags))
                await _tagWriter.WriteTagsAsync(Row.Id);
            _activity.Confirm(string.Format(Strings.Confirm_TagsWritten, Title));
        }
        catch (Exception ex)
        {
            Notice = string.Format(Strings.Notice_WriteTagsFailed, ex.Message);
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void StartAddGenre()
    {
        NewGenre = "";
        IsAddingGenre = true;
    }

    [RelayCommand]
    private void CancelAddGenre() => IsAddingGenre = false;

    [RelayCommand]
    private async Task AddGenreAsync()
    {
        var genre = NewGenre.Trim();
        IsAddingGenre = false;
        if (genre.Length == 0 || Genres.Contains(genre, StringComparer.CurrentCultureIgnoreCase))
            return;
        await SaveGenresAsync([.. Genres, genre]);
    }

    [RelayCommand]
    private Task RemoveGenreAsync(string genre) =>
        SaveGenresAsync(Genres.Where(g => g != genre).ToList());

    partial void OnRatingChanged(int? value)
    {
        if (!_loading)
            _ = SaveRatingAsync(value);
    }

    private async Task SaveRatingAsync(int? rating)
    {
        await RunSaveAsync(() => _repository.SetRatingAsync(Row.Id, rating));
    }

    /// <summary>
    /// Editing any genre turns the shown list into the user's own. An empty list removes the user's genres,
    /// so the next best source shows again.
    /// </summary>
    private Task SaveGenresAsync(IReadOnlyList<string> genres) =>
        RunSaveAsync(() => _repository.SetUserTagsAsync(Row.Id, genres));

    private void ScheduleSave()
    {
        if (_loading)
            return;

        _pendingSave?.Cancel();
        var cts = _pendingSave = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(cts);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SaveDelay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_pendingSave == cts)
                _pendingSave = null;
            cts.Dispose();
        }

        await SaveFieldsAsync();
    }

    /// <summary>Saves edits still waiting for the typing pause before an action that relies on them.</summary>
    private async Task SaveNowAsync()
    {
        if (_pendingSave is null)
            return;
        _pendingSave.Cancel();
        _pendingSave = null;
        await SaveFieldsAsync();
    }

    private async Task SaveFieldsAsync()
    {
        int? year = null;
        if (YearField.UserValue is { } yearText)
        {
            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1000 or > 9999)
            {
                Notice = string.Format(Strings.Detail_InvalidYear, yearText);
                return;
            }
            year = parsed;
        }

        var fields = new UserFields(ArtistField.UserValue, TitleField.UserValue, AlbumField.UserValue,
            AlbumArtistField.UserValue, year, _notes);
        await RunSaveAsync(() => _repository.SetUserFieldsAsync(Row.Id, fields));
    }

    private async Task RunSaveAsync(Func<Task> save)
    {
        Notice = null;
        try
        {
            await save();
        }
        catch (Exception ex)
        {
            Notice = string.Format(Strings.Notice_SaveFailed, ex.Message);
            return;
        }

        await LoadAsync();
        await _actions.RefreshAsync(Row);
    }

    private void LoadField(MetadataFieldViewModel field, string? value, FieldSource source)
    {
        // text typed while the last save ran is saved after the next pause; don't overwrite it
        if (_pendingSave is not null && field.Text != (value ?? ""))
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
