using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
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

/// <summary>Settings page: library folder, language, accent and the bundled tools.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore store;
    private readonly AppPaths paths;
    private readonly LibraryMover mover;
    private readonly ToolService tools;
    private readonly IAccentService accent;
    private readonly ActivityService activity;
    private readonly IPlatformService platform;

    public SettingsViewModel(SettingsStore store, AppPaths paths, LibraryMover mover, ToolService tools,
        IAccentService accent, ActivityService activity, IPlatformService platform)
    {
        this.store = store;
        this.paths = paths;
        this.mover = mover;
        this.tools = tools;
        this.accent = accent;
        this.activity = activity;
        this.platform = platform;

        this.Accents = accent.PresetHexes.Select(hex => new AccentOptionViewModel(hex, this.SelectAccent)).ToList();
        this.Language = FromCode(store.Load().Language);
        this.LibraryDir = paths.LibraryDir;
        this.MarkSelectedAccent();
        activity.PropertyChanged += this.OnActivityChanged;
    }

    public IReadOnlyList<AccentOptionViewModel> Accents { get; }

    [ObservableProperty]
    public partial LanguageChoice Language { get; set; }

    [ObservableProperty]
    public partial string CustomAccent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LibraryDir { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateYtDlpCommand))]
    public partial string? YtDlpVersion { get; private set; }

    [ObservableProperty]
    public partial string? FFmpegVersion { get; private set; }

    [ObservableProperty]
    public partial string? DenoVersion { get; private set; }

    /// <summary>Last error, shown below the header.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Reads the tool versions; called whenever the page is shown.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var versions = await this.tools.GetVersionsAsync();
        this.YtDlpVersion = versions.YtDlp;
        this.FFmpegVersion = versions.FFmpeg;
        this.DenoVersion = versions.Deno;
    }

    partial void OnLanguageChanged(LanguageChoice value) =>
        this.store.Update(s => s with { Language = ToCode(value) });

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        if (this.CustomAccent.Trim().Length == 0)
            return;
        this.SelectAccent(this.CustomAccent);
    }

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
            int moved;
            using (var progress = this.activity.Begin(string.Format(Strings.Activity_MovingLibrary, 0, "…")))
            {
                moved = await this.mover.MoveAsync(folder, new Progress<(int Done, int Total)>(p =>
                    progress.Update(string.Format(Strings.Activity_MovingLibrary, p.Done, p.Total))));
            }

            this.store.Update(s => s with { LibraryDir = folder });
            this.LibraryDir = folder;
            this.activity.Confirm(string.Format(Strings.Confirm_LibraryMoved, moved));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_MoveFailed, ex.Message);
        }
    }

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
