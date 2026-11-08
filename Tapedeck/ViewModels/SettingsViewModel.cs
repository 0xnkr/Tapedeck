using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.Playlists;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Resources;
using Tapedeck.Services;
using Tapedeck.Theme;

namespace Tapedeck.ViewModels;

/// <summary>UI languages offered in the settings.</summary>
public enum LanguageChoice
{
    System,
    English,
    German,
}

/// <summary>One accent swatch in the settings.</summary>
public partial class AccentOptionViewModel(string hex, Action<string> select) : ViewModelBase
{
    public string Hex { get; } = hex;

    public string Label => string.Format(Strings.Settings_AccentPreset, this.Hex);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(this.Hex);
}

/// <summary>One open source project in the credits; opens its website or its license text.</summary>
public partial class CreditViewModel(Credit credit, IPlatformService platform, Func<Credit, Task> showLicense) : ViewModelBase
{
    public string Name => credit.Name;

    public string Purpose => credit.Purpose;

    public string License => credit.License;

    public string OpenLabel => string.Format(Strings.Settings_CreditOpen, credit.Name);

    public string LicenseLabel => string.Format(Strings.Settings_CreditLicense, credit.License, credit.Name);

    [RelayCommand]
    private Task OpenAsync() => platform.OpenUrlAsync(credit.Url);

    [RelayCommand]
    private Task ShowLicenseAsync() => showLicense(credit);
}

/// <summary>Settings page: library folder, language, accent, the bundled tools and the open source credits.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore store;
    private readonly AppPaths paths;
    private readonly LibraryMover mover;
    private readonly ToolService tools;
    private readonly IAccentService accent;
    private readonly ActivityService activity;
    private readonly IPlatformService platform;
    private readonly PlaylistSync playlistSync;
    private readonly DialogService dialogs;
    private readonly PlayerService player;
    private readonly LastFmQueue lastFm;
    private readonly LastFmGenreService lastFmGenres;

    private const string LASTFM_KEY_URL = "https://www.last.fm/api/account/create";

    public SettingsViewModel(SettingsStore store, AppPaths paths, LibraryMover mover, ToolService tools,
        IAccentService accent, ActivityService activity, IPlatformService platform, PlaylistSync playlistSync,
        DialogService dialogs, PlayerService player, LastFmQueue lastFm, LastFmGenreService lastFmGenres)
    {
        this.player = player;
        this.lastFm = lastFm;
        this.lastFmGenres = lastFmGenres;
        this.store = store;
        this.paths = paths;
        this.mover = mover;
        this.tools = tools;
        this.accent = accent;
        this.activity = activity;
        this.platform = platform;
        this.playlistSync = playlistSync;
        this.dialogs = dialogs;

        this.Accents = accent.PresetHexes.Select(hex => new AccentOptionViewModel(hex, this.SelectAccent)).ToList();
        this.OwnCredit = this.NewCredit(Tapedeck.Credits.TAPEDECK);
        this.Credits = Tapedeck.Credits.ALL.Select(this.NewCredit).ToList();
        this.AlsoIncluded = Tapedeck.Credits.ALSO_INCLUDED.Select(this.NewCredit).ToList();
        var settings = store.Load();
        this.Language = FromCode(settings.Language);
        this.MusicBrainzAuto = settings.MusicBrainzAuto ?? true;
        this.AskBeforeKeep = settings.KeepDeletesFiles is null;
        this.LevelPlayback = settings.LevelPlayback ?? true;
        this.LastFmApiKey = settings.LastFmApiKey ?? string.Empty;
        lastFm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LastFmQueue.IsRunning) && !lastFm.IsRunning)
                _ = this.CountLastFmPendingAsync();
        };
        this.LibraryDir = paths.LibraryDir;
        this.MarkSelectedAccent();
        activity.PropertyChanged += this.OnActivityChanged;
    }

    public IReadOnlyList<AccentOptionViewModel> Accents { get; }

    /// <summary>Tapedeck's own license, above the credits.</summary>
    public CreditViewModel OwnCredit { get; }

    public IReadOnlyList<CreditViewModel> Credits { get; }

    /// <summary>Libraries that come along with the credited ones.</summary>
    public IReadOnlyList<CreditViewModel> AlsoIncluded { get; }

    [ObservableProperty]
    public partial LanguageChoice Language { get; set; }

    [ObservableProperty]
    public partial string CustomAccent { get; set; } = string.Empty;

    /// <summary>Look up new tracks on MusicBrainz after an import.</summary>
    [ObservableProperty]
    public partial bool MusicBrainzAuto { get; set; }

    /// <summary>The player plays all tracks equally loud, from the next track on.</summary>
    [ObservableProperty]
    public partial bool LevelPlayback { get; set; }

    /// <summary>"Keep this one" asks before removing duplicates; switched off by "don't ask again" in that dialog.</summary>
    [ObservableProperty]
    public partial bool AskBeforeKeep { get; set; }

    [ObservableProperty]
    public partial string LibraryDir { get; private set; }

    /// <summary>Version and build of the running app, e.g. <c>1.0.0 · 2e02d19</c>.</summary>
    public string AppVersion => AppInfo.DISPLAY;

    /// <summary>The log can be opened; without write access to the app data folder there is none.</summary>
    public bool HasLog => FileLog.Current is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial string? YtDlpVersion { get; private set; }

    [ObservableProperty]
    public partial string? FFmpegVersion { get; private set; }

    [ObservableProperty]
    public partial string? DenoVersion { get; private set; }

    /// <summary>The user's own Last.fm API key; empty turns Last.fm off.</summary>
    [ObservableProperty]
    public partial string LastFmApiKey { get; set; } = string.Empty;

    /// <summary>Tracks without genres that Last.fm was not asked for yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastFmFetchLabel))]
    [NotifyCanExecuteChangedFor(nameof(FetchLastFmCommand))]
    public partial int LastFmPending { get; private set; }

    public string LastFmFetchLabel => string.Format(Strings.Settings_LastFmFetch, this.LastFmPending);

    private bool CanFetchLastFm => this.LastFmPending > 0 && this.LastFmApiKey.Trim().Length > 0;

    /// <summary>Last error, shown below the header.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Reads the tool versions; called whenever the page is shown.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        // the duplicate dialog may have been told not to ask again meanwhile
        this.AskBeforeKeep = this.store.Load().KeepDeletesFiles is null;
        var versions = await this.tools.GetVersionsAsync();
        this.YtDlpVersion = versions.YtDlp;
        this.FFmpegVersion = versions.FFmpeg;
        this.DenoVersion = versions.Deno;
        await this.CountLastFmPendingAsync();
    }

    private async Task CountLastFmPendingAsync() => this.LastFmPending = await this.lastFmGenres.CountPendingAsync();

    partial void OnLastFmApiKeyChanged(string value)
    {
        var key = value.Trim();
        this.store.Update(s => s with { LastFmApiKey = key.Length > 0 ? key : null });
        this.FetchLastFmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Asks Last.fm for the genres of all tracks that have none yet.</summary>
    [RelayCommand(CanExecute = nameof(CanFetchLastFm))]
    private void FetchLastFm() => this.lastFm.EnqueueMissing();

    [RelayCommand]
    private Task OpenLastFmKeyPageAsync() => this.platform.OpenUrlAsync(LASTFM_KEY_URL);

    partial void OnLanguageChanged(LanguageChoice value) =>
        this.store.Update(s => s with { Language = ToCode(value) });

    partial void OnMusicBrainzAutoChanged(bool value) =>
        this.store.Update(s => s with { MusicBrainzAuto = value });

    partial void OnLevelPlaybackChanged(bool value) =>
        this.store.Update(s => s with { LevelPlayback = value });

    // without asking, the copies' files go too, as the dialog suggests
    partial void OnAskBeforeKeepChanged(bool value) =>
        this.store.Update(s => s with { KeepDeletesFiles = value ? null : s.KeepDeletesFiles ?? true });

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        if (this.CustomAccent.Trim().Length == 0)
            return;
        this.SelectAccent(this.CustomAccent);
    }

    private CreditViewModel NewCredit(Credit credit) => new(credit, this.platform, this.ShowLicenseAsync);

    private Task ShowLicenseAsync(Credit credit) =>
        this.dialogs.ShowLicenseAsync(new LicenseDialogViewModel(credit, this.platform, this.activity));

    private void SelectAccent(string hex)
    {
        if (!this.accent.TryApply(hex))
        {
            this.Notice = string.Format(Strings.Settings_AccentInvalid, hex);
            return;
        }

        this.Notice = null;
        this.store.Update(s => s with { Accent = this.accent.AccentHex });
        this.MarkSelectedAccent();
    }

    private void MarkSelectedAccent()
    {
        var current = this.accent.AccentHex;
        foreach (var option in this.Accents)
            option.IsSelected = string.Equals(option.Hex, current, StringComparison.OrdinalIgnoreCase);
        this.CustomAccent = this.Accents.Any(o => o.IsSelected) ? string.Empty : current;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ChangeLibraryAsync()
    {
        var folder = await this.platform.PickFolderAsync(Strings.Settings_ChooseFolder, this.paths.LibraryDir);
        if (folder is null)
            return;

        this.Notice = null;
        var check = await this.mover.CheckAsync(folder);
        if (!check.IsOk)
        {
            this.Notice = check.Problem switch
            {
                MoveProblem.SameFolder => Strings.Move_SameFolder,
                MoveProblem.InsideCurrentFolder => Strings.Move_InsideCurrentFolder,
                MoveProblem.NotWritable => Strings.Move_NotWritable,
                _ => string.Format(Strings.Move_NameTaken, check.FileName),
            };
            return;
        }

        try
        {
            // the playing file moves too, so it has to be closed first
            await this.player.StopAsync();
            int moved;
            var oldDir = this.paths.LibraryDir;
            using (var progress = this.activity.Begin(string.Format(Strings.Activity_MovingLibrary, 0, "…")))
            {
                moved = await this.mover.MoveAsync(folder, new Progress<(int Done, int Total)>(p =>
                    progress.Update(string.Format(Strings.Activity_MovingLibrary, p.Done, p.Total))));
            }

            this.store.Update(s => s with { LibraryDir = folder });
            // the .m3u8 files are written anew next to the moved tracks
            PlaylistExporter.RemoveExported(oldDir);
            await this.playlistSync.SyncNowAsync();
            this.LibraryDir = folder;
            this.activity.Confirm(string.Format(Strings.Confirm_LibraryMoved, moved));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_MoveFailed, ex.Message);
        }
    }

    /// <summary>Opens <c>log.txt</c> in the text editor, e.g. to attach it to a bug report.</summary>
    [RelayCommand]
    private Task OpenLogAsync() =>
        FileLog.Current is { } log ? this.platform.OpenFileAsync(log.Path) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanUpdateYtDlp))]
    private async Task UpdateYtDlpAsync()
    {
        this.Notice = null;
        try
        {
            using (this.activity.Begin(Strings.Activity_UpdatingYtDlp))
                await this.tools.UpdateYtDlpAsync();
            await this.LoadAsync();
            this.activity.Confirm(string.Format(Strings.Confirm_YtDlpUpdated, this.YtDlpVersion));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_UpdateFailed, ex.Message);
        }
    }

    private bool IsIdle() => !this.activity.IsBusy;

    private bool CanUpdateYtDlp() => this.YtDlpVersion is not null && this.IsIdle();

    private void OnActivityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ActivityService.IsBusy))
            return;
        this.ChangeLibraryCommand.NotifyCanExecuteChanged();
        this.UpdateYtDlpCommand.NotifyCanExecuteChanged();
    }

    private static LanguageChoice FromCode(string? code) => code switch
    {
        "en" => LanguageChoice.English,
        "de" => LanguageChoice.German,
        _ => LanguageChoice.System,
    };

    private static string? ToCode(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => "en",
        LanguageChoice.German => "de",
        _ => null,
    };
}
