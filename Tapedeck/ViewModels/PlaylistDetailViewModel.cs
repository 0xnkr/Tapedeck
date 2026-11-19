using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

/// <summary>A track at its place in a playlist; the row is shared with the library, so status and progress stay in sync.</summary>
public sealed class PlaylistEntryViewModel(int position, TrackRowViewModel row) : ViewModelBase
{
    public int Position { get; } = position;

    /// <summary>1-based position as shown in the table.</summary>
    public int Number => this.Position + 1;

    public TrackRowViewModel Row { get; } = row;
}

/// <summary>One playlist: its tracks in order, reorder, remove, download, rename and delete; sync when linked to YouTube.</summary>
public partial class PlaylistDetailViewModel(
    long playlistId,
    IPlaylistRepository repository,
    PlaylistImportService importer,
    CsvImportService csvImporter,
    LibraryViewModel library,
    PlaylistsViewModel playlists,
    ActivityService activity,
    NavigationService navigation) : ViewModelBase
{
    // the playlist's YouTube source, null when it was made in the app
    private string? sourceUrl;
    private bool listening;

    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>The focused entry (the first selected one); moving and "back" use it.</summary>
    [ObservableProperty]
    public partial PlaylistEntryViewModel? SelectedEntry { get; set; }

    /// <summary>All selected entries (Ctrl+click, Shift+click), kept in sync by the list.</summary>
    public ObservableCollection<PlaylistEntryViewModel> SelectedEntries { get; } = [];

    /// <summary>More than one entry is selected in a playlist made by hand; the header offers to take them out.</summary>
    public bool CanRemoveMany => this.IsEditable && this.SelectedEntries.Count > 1;

    public string RemoveSelectedText => string.Format(Strings.Playlist_RemoveSelected_Other, this.SelectedEntries.Count);

    public bool IsEmpty => this.Entries.Count == 0;

    /// <summary>Linked to a YouTube playlist, so it can be synced.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    public partial bool IsLinked { get; private set; }

    /// <summary>Fills itself by rules: no reordering, removing or renaming here, "Edit rules" instead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(EmptyText), nameof(CanRemoveMany))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial bool IsSmart { get; private set; }

    /// <summary>Tracks can be reordered and removed by hand.</summary>
    public bool IsEditable => !this.IsSmart;

    public string EmptyText => this.IsSmart ? Strings.Playlist_SmartEmpty : Strings.Playlist_Empty;

    /// <summary>Videos that could not be added during the last import or sync.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    public partial bool IsSyncing { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (await repository.GetAsync(playlistId) is not { } playlist)
        {
            navigation.Back();
            return;
        }

        if (!this.listening)
        {
            this.listening = true;
            this.SelectedEntries.CollectionChanged += (_, _) => this.OnSelectionChanged();
        }

        // rebuilding the list clears its selection; the tracks still in it stay selected
        var selectedTrack = this.SelectedEntry?.Row.Id;
        var selectedTracks = this.SelectedEntries.Select(e => e.Row.Id).ToHashSet();
        var rows = (await repository.GetTracksAsync(playlistId))
            .Select(track => library.RowFor(track.Id))
            .OfType<TrackRowViewModel>()
            .ToList();

        this.Name = playlist.Name;
        this.sourceUrl = playlist.SourceUrl;
        this.IsLinked = playlist.SourceUrl is not null;
        this.IsSmart = playlist.IsSmart;
        this.Entries.Clear();
        for (var i = 0; i < rows.Count; i++)
            this.Entries.Add(new PlaylistEntryViewModel(i, rows[i]));
        this.SelectedEntries.Clear();
        // setting the focused entry selects it alone, so the others are added after it
        this.SelectedEntry = this.Entries.FirstOrDefault(e => e.Row.Id == selectedTrack);
        foreach (var entry in this.Entries.Where(e => selectedTracks.Contains(e.Row.Id) && !this.SelectedEntries.Contains(e)))
            this.SelectedEntries.Add(entry);
        this.Summary = PlaylistsViewModel.SummaryOf(rows.Count, rows.Count(r => r.Status == DownloadStatus.Done), this.IsLinked, this.IsSmart);
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    [RelayCommand]
    private void Back() => navigation.Back();

    /// <summary>Marks a track, e.g. the one last shown on the detail page, so going back finds it.</summary>
    public void SelectRow(TrackRowViewModel row)
    {
        var entry = this.Entries.FirstOrDefault(e => e.Row == row);
        this.SelectedEntries.Clear();
        this.SelectedEntry = entry;
        // the list adds it itself while it is shown; otherwise the selection waits here for it
        if (entry is not null && !this.SelectedEntries.Contains(entry))
            this.SelectedEntries.Add(entry);
    }

    /// <summary>Reads the YouTube playlist again and adds its new videos.</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (!PlaylistLink.TryParse(this.sourceUrl, out var listId))
            return;

        this.Notice = null;
        YtPlaylist source;
        try
        {
            source = await playlists.FetchPlaylistAsync(listId);
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_PlaylistReadFailed, ex.Message);
            return;
        }
        await this.ImportAsync(source);
    }

    private bool CanSync() => this.IsLinked && !this.IsSyncing;

    /// <summary>
    /// Appends the videos of the YouTube playlist that are not in this playlist yet; new ones are fetched like a
    /// pasted link. Videos that fail are listed in <see cref="Notice"/> and tried again on the next sync.
    /// </summary>
    public async Task ImportAsync(YtPlaylist source)
    {
        // right after creating the playlist, its page may not have loaded the name yet
        var name = this.Name.Length > 0 ? this.Name : source.Title ?? string.Empty;
        this.Notice = null;
        this.IsSyncing = true;
        PlaylistImportResult result;
        try
        {
            using var progress = activity.Begin(string.Format(Strings.Activity_ImportingPlaylist, name, 0, source.Entries.Count));
            result = await importer.ImportAsync(playlistId, source, new Progress<(int Done, int Total)>(p =>
                progress.Update(string.Format(Strings.Activity_ImportingPlaylist, name, p.Done, p.Total))));
        }
        catch (Exception ex)
        {
            this.Notice = string.Format(Strings.Notice_PlaylistImportFailed, ex.Message);
            return;
        }
        finally
        {
            this.IsSyncing = false;
            // also after a failure: some tracks may be in the library already
            await library.AddNewTracksAsync();
            await this.ChangedAsync();
        }

        this.Notice = result.Errors.Count == 0 ? null : string.Format(Strings.Notice_PlaylistVideosFailed, result.Errors.Count,
            string.Join("; ", result.Errors.Take(3).Select(e => string.Format(Strings.Playlist_VideoError, e.Title ?? e.VideoId, e.Message)))
            + (result.Errors.Count > 3 ? " …" : string.Empty));
        activity.Confirm(result.Added switch
        {
            0 => string.Format(Strings.Confirm_PlaylistUpToDate, name),
            1 => string.Format(Strings.Confirm_PlaylistImported_One, name),
            _ => string.Format(Strings.Confirm_PlaylistImported_Other, name, result.Added),
        });
    }

    /// <summary>
    /// Adds the tracks of a CSV file in file order, like the library's CSV import (corrections, genres, ratings);
    /// files already in the library folder are taken over. Rows that fail are listed in <see cref="Notice"/>.
    /// </summary>
    public async Task ImportCsvAsync(CsvReadResult csv, string name)
    {
        var errors = csv.Errors.Select(LibraryViewModel.Describe).ToList();
        var trackIds = new List<long>();
        var added = 0;
        this.Notice = null;
        this.IsSyncing = true;
        try
        {
            CsvImportResult result;
            using (var progress = activity.Begin(string.Format(Strings.Activity_Importing, 0, csv.Tracks.Count)))
                result = await csvImporter.ImportAsync(csv.Tracks, new Progress<int>(done =>
                    progress.Update(string.Format(Strings.Activity_Importing, done, csv.Tracks.Count))));
            trackIds.AddRange(result.TrackIds);
            errors.AddRange(result.Errors.Select(LibraryViewModel.Describe));
            added = await repository.AddTracksAsync(playlistId, result.TrackIds);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }
        finally
        {
            this.IsSyncing = false;
            await library.RefreshTracksAsync(trackIds);
            await this.ChangedAsync();
        }

        if (errors.Count > 0)
        {
            var shown = string.Join("; ", errors.Take(3)) + (errors.Count > 3 ? " …" : string.Empty);
            this.Notice = string.Format(Strings.Notice_ImportResult, trackIds.Count, errors.Count, shown);
        }
        activity.Confirm(added == 1
            ? string.Format(Strings.Confirm_PlaylistImported_One, name)
            : string.Format(Strings.Confirm_PlaylistImported_Other, name, added));
    }

    /// <summary>Downloads the playlist's pending and missing tracks with the library's download logic.</summary>
    [RelayCommand]
    private async Task DownloadAsync()
    {
        var rows = this.Entries
            .Select(e => e.Row)
            .Where(r => r.Status is DownloadStatus.Pending or DownloadStatus.Missing)
            .ToList();
        await library.DownloadTracksAsync(rows);
        await this.LoadAsync();
    }

    /// <summary>Exports the playlist's tracks in their order as CSV, as <c>Tapedeck-name.csv</c>.</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        this.Notice = await library.SaveCsvAsync(() => repository.GetTracksAsync(playlistId),
            string.Format(Strings.Csv_PlaylistFileName, FileNaming.Slug(this.Name)));
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (await playlists.RenameWithPromptAsync(playlistId, this.Name))
            await this.LoadAsync();
    }

    [RelayCommand]
    private void EditRules() => playlists.OpenSmartEditor(playlistId);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (await playlists.DeleteWithConfirmAsync(playlistId, this.Name))
            navigation.Back();
    }

    [RelayCommand]
    private async Task RemoveTrackAsync(PlaylistEntryViewModel entry)
    {
        if (this.IsSmart)
            return;
        await repository.RemoveTrackAsync(playlistId, entry.Row.Id);
        await this.ChangedAsync();
    }

    /// <summary>
    /// Takes the selected tracks out of the playlist (header button, Delete key); they stay in the library.
    /// Smart playlists pick their tracks by rules, so nothing can be taken out by hand.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveSelectedAsync()
    {
        var entries = this.SelectedEntries.ToList();
        if (this.IsSmart || entries.Count == 0)
            return;
        foreach (var entry in entries)
            await repository.RemoveTrackAsync(playlistId, entry.Row.Id);
        await this.ChangedAsync();
        activity.Confirm(entries.Count == 1
            ? Strings.Confirm_PlaylistRemoved_One
            : string.Format(Strings.Confirm_PlaylistRemoved_Other, entries.Count));
    }

    private bool CanRemoveSelected() => this.IsEditable && this.SelectedEntries.Count > 0;

    private void OnSelectionChanged()
    {
        this.OnPropertyChanged(nameof(this.CanRemoveMany));
        this.OnPropertyChanged(nameof(this.RemoveSelectedText));
        this.RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Moves a track to another place (drag and drop or Alt+Up/Down).</summary>
    public async Task MoveAsync(PlaylistEntryViewModel entry, int newIndex)
    {
        if (this.IsSmart || newIndex == entry.Position || newIndex < 0 || newIndex >= this.Entries.Count)
            return;
        await repository.MoveTrackAsync(playlistId, entry.Row.Id, newIndex);
        this.SelectedEntry = entry;
        await this.ChangedAsync();
    }

    [RelayCommand]
    private Task MoveUpAsync() => this.SelectedEntry is { } entry ? this.MoveAsync(entry, entry.Position - 1) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDownAsync() => this.SelectedEntry is { } entry ? this.MoveAsync(entry, entry.Position + 1) : Task.CompletedTask;

    /// <summary>The phone's row menu: one place up, as dragging is unreliable with a finger.</summary>
    [RelayCommand]
    private Task MoveEntryUpAsync(PlaylistEntryViewModel entry) => this.MoveAsync(entry, entry.Position - 1);

    /// <summary>The phone's row menu: one place down.</summary>
    [RelayCommand]
    private Task MoveEntryDownAsync(PlaylistEntryViewModel entry) => this.MoveAsync(entry, entry.Position + 1);

    private async Task ChangedAsync()
    {
        await this.LoadAsync();
        await playlists.ChangedAsync();
    }
}
