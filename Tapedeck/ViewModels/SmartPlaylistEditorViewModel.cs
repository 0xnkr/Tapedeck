using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Creates the editor for a new smart playlist (<c>0</c>) or for the rules of an existing one.</summary>
public delegate SmartPlaylistEditorViewModel SmartPlaylistEditorFactory(long playlistId);

/// <summary>
/// Name and rules of a smart playlist, with a live preview of the tracks they pick from the library.
/// Saving goes through <see cref="PlaylistsViewModel"/>, which checks the name like for every playlist.
/// </summary>
public partial class SmartPlaylistEditorViewModel(
    long playlistId,
    IPlaylistRepository repository,
    ITrackRepository tracks,
    PlaylistsViewModel playlists,
    NavigationService navigation) : ViewModelBase
{
    private const int PREVIEW_SIZE = 12;

    private IReadOnlyList<TrackDisplay> library = [];
    // rules report changes while they are built; the preview waits until everything is loaded
    private bool ready;

    public bool IsNew => playlistId == 0;

    public string Title => this.IsNew ? Strings.Smart_NewTitle : Strings.Smart_EditTitle;

    public ObservableCollection<SmartRuleViewModel> Rules { get; } = [];

    public ObservableCollection<MenuChoice> SortChoices { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SmartMatch Match { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortText))]
    public partial SmartSort Sort { get; set; }

    public string SortText => SmartLabels.Of(this.Sort);

    /// <summary>Empty for all tracks.</summary>
    [ObservableProperty]
    public partial string LimitText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewCount { get; private set; } = string.Empty;

    /// <summary>"Artist – Title" of the first tracks.</summary>
    public ObservableCollection<string> PreviewTracks { get; } = [];

    /// <summary>"… and 40 more", or <c>null</c> when all are shown.</summary>
    [ObservableProperty]
    public partial string? PreviewMore { get; private set; }

    [ObservableProperty]
    public partial bool HasNoRules { get; private set; }

    /// <summary>Why the playlist cannot be saved yet.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        this.library = await tracks.ListDisplayAsync();
        var rules = SmartRules.Empty with { Rules = [new SmartRule(SmartField.Genre, SmartOperator.Contains, string.Empty)] };
        if (!this.IsNew && await repository.GetAsync(playlistId) is { IsSmart: true } playlist)
        {
            this.Name = playlist.Name;
            rules = SmartRules.FromJson(playlist.Rules);
        }

        this.SortChoices.Clear();
        foreach (var sort in Enum.GetValues<SmartSort>())
            this.SortChoices.Add(new MenuChoice(SmartLabels.Of(sort), new RelayCommand(() => this.Sort = sort)));

        this.Match = rules.Match;
        this.Sort = rules.Sort;
        this.LimitText = rules.Limit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.Rules.Clear();
        foreach (var rule in rules.Rules)
            this.Rules.Add(this.NewRule(rule));

        this.ready = true;
        this.UpdatePreview();
    }

    [RelayCommand]
    private void AddRule()
    {
        this.Rules.Add(this.NewRule(new SmartRule(SmartField.Genre, SmartOperator.Contains, string.Empty)));
        this.UpdatePreview();
    }

    [RelayCommand]
    private void Cancel() => navigation.Back();

    [RelayCommand]
    private async Task SaveAsync()
    {
        this.Notice = this.Problem();
        if (this.Notice is not null)
            return;

        var rules = this.Build(skipIncomplete: false);
        var (id, error) = await playlists.SaveSmartAsync(this.IsNew ? null : playlistId, this.Name.Trim(), rules);
        if (error is not null)
        {
            this.Notice = error;
            return;
        }

        navigation.Back();
        if (this.IsNew)
            playlists.OpenPlaylist(id);
        else if (navigation.Current is PlaylistDetailViewModel detail)
            detail.LoadCommand.Execute(null);
    }

    partial void OnMatchChanged(SmartMatch value) => this.UpdatePreview();

    partial void OnSortChanged(SmartSort value) => this.UpdatePreview();

    partial void OnLimitTextChanged(string value) => this.UpdatePreview();

    private SmartRuleViewModel NewRule(SmartRule rule) => new(rule, this.UpdatePreview, this.RemoveRule);

    private void RemoveRule(SmartRuleViewModel rule)
    {
        this.Rules.Remove(rule);
        this.UpdatePreview();
    }

    /// <summary>Shows what the rules pick right now; rules still without a value are left out.</summary>
    private void UpdatePreview()
    {
        if (!this.ready)
            return;

        var rules = this.Build(skipIncomplete: true);
        var picked = rules.Apply(this.library, DateTime.UtcNow);
        this.HasNoRules = rules.Rules.Count == 0;
        this.PreviewCount = string.Format(picked.Count == 1 ? Strings.Smart_PreviewCount_One : Strings.Smart_PreviewCount_Other, picked.Count);
        this.PreviewTracks.Clear();
        foreach (var track in picked.Take(PREVIEW_SIZE))
            this.PreviewTracks.Add($"{track.Artist} – {track.Title}");
        this.PreviewMore = picked.Count > PREVIEW_SIZE ? string.Format(Strings.Smart_PreviewMore, picked.Count - PREVIEW_SIZE) : null;
    }

    private SmartRules Build(bool skipIncomplete)
    {
        var rules = this.Rules.Select(r => r.ToRule());
        if (skipIncomplete)
            rules = rules.Where(IsComplete);
        return new SmartRules(this.Match, rules.ToList(), this.Sort, ParseLimit(this.LimitText));
    }

    /// <summary>The first thing the user still has to fix, or <c>null</c>.</summary>
    private string? Problem()
    {
        for (var i = 0; i < this.Rules.Count; i++)
        {
            var rule = this.Rules[i].ToRule();
            if (rule.Value.Length == 0)
                return string.Format(Strings.Smart_RuleEmpty, i + 1);
            if (SmartRules.IsNumeric(rule.Field) && SmartRules.NumberOf(rule) is null)
                return string.Format(Strings.Smart_RuleNotNumber, i + 1, rule.Value);
        }

        var limit = this.LimitText.Trim();
        return limit.Length > 0 && ParseLimit(limit) is null ? string.Format(Strings.Smart_LimitInvalid, limit) : null;
    }

    private static bool IsComplete(SmartRule rule) =>
        rule.Value.Length > 0 && (!SmartRules.IsNumeric(rule.Field) || SmartRules.NumberOf(rule) is not null);

    private static int? ParseLimit(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0 ? limit : null;
}
