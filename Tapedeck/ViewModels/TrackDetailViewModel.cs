using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Services;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Creates the detail page for a library row; the row's page carries out downloads and file actions.</summary>
public delegate TrackDetailViewModel TrackDetailFactory(TrackRowViewModel row, ITrackActions actions, TrackSequence sequence);

/// <summary>
/// The list the details were opened from (the filtered library or a playlist), for going to the previous and next
/// track. <paramref name="Select"/> marks the shown track in that list, so going back finds it.
/// </summary>
public sealed record TrackSequence(IReadOnlyList<TrackRowViewModel> Rows, Action<TrackRowViewModel> Select);

/// <summary>A song MusicBrainz suggests for the track, with the button that applies it.</summary>
/// <param name="Title">"Alright — Kendrick Lamar"</param>
/// <param name="Details">"To Pimp a Butterfly · 2015 · 3:39 · 82 %"</param>
public sealed record MbCandidateViewModel(string Title, string Details, IAsyncRelayCommand ApplyCommand);

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
    private readonly LibraryFiles files;
    private readonly ActivityService activity;
    private readonly NavigationService navigation;
    private readonly MbEnrichmentService musicBrainz;
    private readonly IPlatformService platform;
    private readonly PlayerService player;
    private readonly TrackSequence sequence;
    private readonly TrackDetailFactory createDetail;

    private readonly LyricsService lyricsService;

    private CancellationTokenSource? pendingSave;
    private CancellationTokenSource? pendingLyricsSave;
    private TrackLyrics lyrics = TrackLyrics.NONE;
    private string? notes;
    private bool loading;
    private string? mbRecordingId;

    public TrackDetailViewModel(TrackRowViewModel row, ITrackActions actions, TrackSequence sequence, TrackDetailFactory createDetail,
        ITrackRepository repository,
        TagWriter tagWriter, LibraryFiles files, ActivityService activity, NavigationService navigation,
        MbEnrichmentService musicBrainz, IPlatformService platform, PlayerService player, LyricsService lyricsService)
    {
        this.lyricsService = lyricsService;
        this.player = player;
        this.sequence = sequence;
        this.createDetail = createDetail;
        this.Index = sequence.Rows.ToList().IndexOf(row);
        this.Row = row;
        this.actions = actions;
        this.repository = repository;
        this.tagWriter = tagWriter;
        this.files = files;
        this.activity = activity;
        this.navigation = navigation;
        this.musicBrainz = musicBrainz;
        this.platform = platform;

        this.ArtistField = new MetadataFieldViewModel(Strings.Field_Artist, this.ScheduleSave);
        this.TitleField = new MetadataFieldViewModel(Strings.Field_Title, this.ScheduleSave);
        this.AlbumField = new MetadataFieldViewModel(Strings.Field_Album, this.ScheduleSave);
        this.AlbumArtistField = new MetadataFieldViewModel(Strings.Field_AlbumArtist, this.ScheduleSave);
        this.YearField = new MetadataFieldViewModel(Strings.Field_Year, this.ScheduleSave);
        this.Fields = [this.ArtistField, this.TitleField, this.AlbumField, this.AlbumArtistField, this.YearField];
    }

    /// <summary>Place of the track in the list it was opened from; -1 when it is not in it (any more).</summary>
    public int Index { get; }

    public bool HasPrevious => this.Index > 0;

    public bool HasNext => this.Index >= 0 && this.Index < this.sequence.Rows.Count - 1;

    /// <summary>"12 / 144"; empty without a list to go through.</summary>
    public string PositionText => this.Index >= 0 && this.sequence.Rows.Count > 1
        ? string.Format(Strings.Detail_Position, this.Index + 1, this.sequence.Rows.Count)
        : string.Empty;

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

    /// <summary>"Main genre in the file: Hip-Hop"; empty when none of the genres maps to a main genre.</summary>
    [ObservableProperty]
    public partial string MainGenres { get; private set; } = string.Empty;

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

    /// <summary>The square cover from MusicBrainz, otherwise the thumbnail that is embedded instead.</summary>
    [ObservableProperty]
    public partial string? CoverPath { get; private set; }

    [ObservableProperty]
    public partial string CoverText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMbMatched), nameof(IsMbSuggested), nameof(MbStateText), nameof(MbHint), nameof(MbSearchText))]
    public partial MbMatchState MbState { get; private set; }

    public bool IsMbMatched => this.MbState == MbMatchState.Matched;

    public bool IsMbSuggested => this.MbState == MbMatchState.Suggested;

    public string MbStateText => this.MbState switch
    {
        MbMatchState.Matched => Strings.Mb_StateMatched,
        MbMatchState.Suggested => Strings.Mb_StateSuggested,
        MbMatchState.NoMatch => Strings.Mb_StateNoMatch,
        MbMatchState.Rejected => Strings.Mb_StateRejected,
        _ => Strings.Mb_StateNone,
    };

    /// <summary>What the state means for the user; <c>null</c> for a match, which shows its release instead.</summary>
    public string? MbHint => this.MbState switch
    {
        MbMatchState.Matched => null,
        MbMatchState.Suggested => Strings.Mb_HintSuggested,
        MbMatchState.NoMatch => Strings.Mb_HintNoMatch,
        MbMatchState.Rejected => Strings.Mb_HintRejected,
        _ => Strings.Mb_HintNone,
    };

    public string MbSearchText => this.MbState == MbMatchState.None ? Strings.Mb_Search : Strings.Mb_SearchAgain;

    /// <summary>"To Pimp a Butterfly · 2015 · Track 7" of the applied match.</summary>
    [ObservableProperty]
    public partial string? MbRelease { get; private set; }

    /// <summary>"Kendrick Lamar — Alright · 97 % match" of the applied match.</summary>
    [ObservableProperty]
    public partial string? MbRecording { get; private set; }

    public ObservableCollection<MbCandidateViewModel> MbCandidates { get; } = [];

    [ObservableProperty]
    public partial bool HasMbCandidates { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchMusicBrainzCommand), nameof(RejectMusicBrainzCommand))]
    public partial bool IsMbBusy { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (await this.repository.GetDisplayAsync(this.Row.Id) is not { } track)
            return;
        var raw = await this.repository.GetAsync(this.Row.Id);
        this.LoadMusicBrainz(track, raw);
        var trackLyrics = await this.repository.GetLyricsAsync(this.Row.Id);

        this.loading = true;
        this.LoadLyrics(trackLyrics);
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
        this.MainGenres = track.MainGenreList.Count == 0 ? string.Empty : string.Format(Strings.Genre_Main, string.Join(", ", track.MainGenreList));

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

    /// <summary>The previous track of the list, in place of this page (button, arrow key left).</summary>
    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private Task PreviousAsync() => this.ShowAsync(this.Index - 1);

    /// <summary>The next track of the list, in place of this page (button, arrow key right).</summary>
    [RelayCommand(CanExecute = nameof(HasNext))]
    private Task NextAsync() => this.ShowAsync(this.Index + 1);

    /// <summary>Saves what is still being typed, then swaps this page for the details of another track of the list.</summary>
    private async Task ShowAsync(int index)
    {
        if (index < 0 || index >= this.sequence.Rows.Count)
            return;
        await this.SaveNowAsync();

        var row = this.sequence.Rows[index];
        this.sequence.Select(row);
        this.navigation.Replace(this.createDetail(row, this.actions, this.sequence));
    }

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
            string? renamed;
            using (this.activity.Begin(Strings.Activity_WritingTags))
            {
                // a playing file is open, so it could neither be renamed nor written
                await this.player.StopIfLoadedAsync(this.Row.Id);
                // the file name follows corrected artist and title, then the tags go in
                renamed = await this.files.RenameToMatchAsync(this.Row.Id);
                await this.tagWriter.WriteTagsAsync(this.Row.Id);
            }
            this.activity.Confirm(renamed is null
                ? string.Format(Strings.Confirm_TagsWritten, this.Title)
                : string.Format(Strings.Confirm_TagsWrittenRenamed, renamed));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_WriteTagsFailed, ex.Message);
        }

        await this.LoadAsync();
        await this.actions.RefreshAsync(this.Row);
    }

    /// <summary>Searches MusicBrainz for this track now, also when it was searched or matched before.</summary>
    [RelayCommand(CanExecute = nameof(IsMbIdle))]
    private Task SearchMusicBrainzAsync() => this.RunMusicBrainzAsync(async () =>
    {
        MbMatchState state;
        using (this.activity.Begin(Strings.Activity_SearchingMusicBrainz))
            state = await this.musicBrainz.EnrichAsync(this.Row.Id);
        if (state == MbMatchState.Matched)
            this.activity.Confirm(string.Format(Strings.Confirm_MbApplied, this.Title));
    });

    /// <summary>Removes the match or the suggestions; a bulk run leaves the track alone afterwards.</summary>
    [RelayCommand(CanExecute = nameof(IsMbIdle))]
    private Task RejectMusicBrainzAsync() => this.RunMusicBrainzAsync(() => this.musicBrainz.RejectAsync(this.Row.Id));

    [RelayCommand]
    private Task OpenMusicBrainzAsync() =>
        this.mbRecordingId is { } id ? this.platform.OpenUrlAsync($"https://musicbrainz.org/recording/{id}") : Task.CompletedTask;

    private bool IsMbIdle() => !this.IsMbBusy;

    private Task ApplyCandidateAsync(MbPick pick) => this.RunMusicBrainzAsync(async () =>
    {
        using (this.activity.Begin(Strings.Activity_SearchingMusicBrainz))
            await this.musicBrainz.ApplyAsync(this.Row.Id, pick);
        this.activity.Confirm(string.Format(Strings.Confirm_MbApplied, pick.Title));
    });

    /// <summary>Saves pending edits first, so a correction typed just before searching counts.</summary>
    private async Task RunMusicBrainzAsync(Func<Task> work)
    {
        await this.SaveNowAsync();
        this.Notice = null;
        this.IsMbBusy = true;
        try
        {
            await work();
        }
        catch (HttpRequestException ex)
        {
            this.Notice = string.Format(Strings.Notice_MusicBrainzFailed, ex.Message);
        }
        finally
        {
            this.IsMbBusy = false;
        }

        await this.LoadAsync();
        await this.actions.RefreshAsync(this.Row);
    }

    private void LoadMusicBrainz(TrackDisplay track, Track? raw)
    {
        this.MbState = track.MbState;
        this.mbRecordingId = track.MbRecordingId;
        this.CoverPath = track.Cover;
        this.CoverText = track.CoverSource switch
        {
            "caa" => Strings.Detail_CoverFromMusicBrainz,
            "thumb" => Strings.Detail_CoverCutFromThumbnail,
            _ => Strings.Detail_CoverFromThumbnail,
        };

        if (raw is not null && track.MbState == MbMatchState.Matched)
        {
            this.MbRelease = JoinParts(raw.MbAlbum, Year(raw.MbReleaseDate),
                raw.MbTrackNo is { } number ? string.Format(Strings.Mb_TrackNumber, number) : null);
            this.MbRecording = JoinParts($"{raw.MbArtist} — {raw.MbTitle}",
                raw.MbScore is { } score ? string.Format(Strings.Mb_Score, Percent(score)) : null);
        }
        else
        {
            this.MbRelease = null;
            this.MbRecording = null;
        }

        this.MbCandidates.Clear();
        if (track.MbState is MbMatchState.Suggested or MbMatchState.Rejected)
        {
            foreach (var pick in MbCandidatesJson.Deserialize(raw?.MbCandidates))
            {
                this.MbCandidates.Add(new MbCandidateViewModel(
                    $"{pick.Title} — {pick.Artist}",
                    JoinParts(pick.Album, Year(pick.Date), Duration(pick.LengthS), string.Format(Strings.Mb_Score, Percent(pick.Score))),
                    new AsyncRelayCommand(() => this.ApplyCandidateAsync(pick), this.IsMbIdle)));
            }
        }
        this.HasMbCandidates = this.MbCandidates.Count > 0;
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string? Year(string? date) => date is { Length: >= 4 } ? date[..4] : null;

    private static int Percent(double score) => (int)Math.Round(score * 100);

    private static string? Duration(int? seconds) =>
        seconds is { } s ? $"{s / 60}:{s % 60:00}" : null;

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
        if (this.pendingLyricsSave is { } lyricsSave)
        {
            lyricsSave.Cancel();
            this.pendingLyricsSave = null;
            await this.SaveLyricsAsync();
        }
        if (this.pendingSave is null)
            return;
        this.pendingSave.Cancel();
        this.pendingSave = null;
        await this.SaveFieldsAsync();
    }

    /// <summary>The shown lyrics: the user's own, else LRCLIB's. Editing them makes them the user's own.</summary>
    [ObservableProperty]
    public partial string LyricsText { get; set; } = string.Empty;

    /// <summary>Where the lyrics come from and whether they are timed, e.g. "LRCLIB · timed".</summary>
    [ObservableProperty]
    public partial string LyricsState { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetLyricsCommand))]
    public partial bool HasUserLyrics { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchLyricsCommand))]
    public partial bool IsLyricsBusy { get; private set; }

    partial void OnLyricsTextChanged(string value)
    {
        if (this.loading)
            return;
        this.pendingLyricsSave?.Cancel();
        var cts = this.pendingLyricsSave = new CancellationTokenSource();
        _ = this.SaveLyricsAfterDelayAsync(cts);
    }

    private async Task SaveLyricsAfterDelayAsync(CancellationTokenSource cts)
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
            if (this.pendingLyricsSave == cts)
                this.pendingLyricsSave = null;
            cts.Dispose();
        }

        await this.SaveLyricsAsync();
    }

    /// <summary>Text equal to LRCLIB's, or none at all, is no correction: LRCLIB's lyrics show again.</summary>
    private Task SaveLyricsAsync()
    {
        var text = Normalize(this.LyricsText);
        var fromLrclib = Normalize(this.lyrics.Synced ?? this.lyrics.Plain ?? string.Empty);
        string? own = text.Length == 0 || text == fromLrclib ? null : text;
        return this.RunSaveAsync(() => this.repository.SetUserLyricsAsync(this.Row.Id, own));
    }

    /// <summary>Drops the user's own lyrics, so LRCLIB's show again.</summary>
    [RelayCommand(CanExecute = nameof(HasUserLyrics))]
    private async Task ResetLyricsAsync()
    {
        this.pendingLyricsSave?.Cancel();
        this.pendingLyricsSave = null;
        await this.RunSaveAsync(() => this.repository.SetUserLyricsAsync(this.Row.Id, null));
    }

    /// <summary>Asks LRCLIB again, e.g. after correcting artist or title.</summary>
    [RelayCommand(CanExecute = nameof(IsLyricsIdle))]
    private async Task SearchLyricsAsync()
    {
        await this.SaveNowAsync();
        this.Notice = null;
        this.IsLyricsBusy = true;
        try
        {
            LrclibLyrics found;
            using (this.activity.Begin(Strings.Activity_SearchingLyrics))
                found = await this.lyricsService.FetchAsync(this.Row.Id);
            this.activity.Confirm(found.Synced is not null ? Strings.Confirm_LyricsTimed
                : found.Plain is not null ? Strings.Confirm_LyricsPlain
                : found.Instrumental ? Strings.Confirm_LyricsInstrumental
                : Strings.Confirm_LyricsNone);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            this.Notice = string.Format(Strings.Notice_LyricsFailed, ex.Message);
        }
        finally
        {
            this.IsLyricsBusy = false;
        }

        await this.LoadAsync();
        await this.actions.RefreshAsync(this.Row);
    }

    private bool IsLyricsIdle() => !this.IsLyricsBusy;

    private void LoadLyrics(TrackLyrics loaded)
    {
        this.lyrics = loaded;
        this.HasUserLyrics = loaded.IsUser;
        this.LyricsState = LyricsStateOf(loaded);
        // lyrics typed while the last save ran are saved after the next pause; don't overwrite them
        if (this.pendingLyricsSave is null)
            this.LyricsText = loaded.Text ?? string.Empty;
    }

    private static string LyricsStateOf(TrackLyrics lyrics)
    {
        if (lyrics.Text is null)
            return lyrics.Instrumental ? Strings.Lyrics_Instrumental
                : lyrics.CheckedAt is null ? Strings.Lyrics_NotSearched
                : Strings.Lyrics_NoneFound;
        var source = lyrics.IsUser ? Strings.Lyrics_SourceUser : Strings.Lyrics_SourceLrclib;
        return string.Format(Strings.Lyrics_State, source, lyrics.IsSynced ? Strings.Lyrics_Timed : Strings.Lyrics_Plain);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

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
