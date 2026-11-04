using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;
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

/// <summary>One playlist: its tracks in order, reorder, remove, download, rename and delete.</summary>
public partial class PlaylistDetailViewModel(
    long playlistId,
    IPlaylistRepository repository,
    LibraryViewModel library,
    PlaylistsViewModel playlists,
    NavigationService navigation) : ViewModelBase
{
    public ObservableCollection<PlaylistEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PlaylistEntryViewModel? SelectedEntry { get; set; }

    public bool IsEmpty => this.Entries.Count == 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (await repository.GetAsync(playlistId) is not { } playlist)
        {
            navigation.Back();
            return;
        }

        var selectedTrack = this.SelectedEntry?.Row.Id;
        var rows = (await repository.GetTracksAsync(playlistId))
            .Select(track => library.RowFor(track.Id))
            .OfType<TrackRowViewModel>()
            .ToList();

        this.Name = playlist.Name;
        this.Entries.Clear();
        for (var i = 0; i < rows.Count; i++)
            this.Entries.Add(new PlaylistEntryViewModel(i, rows[i]));
        this.SelectedEntry = this.Entries.FirstOrDefault(e => e.Row.Id == selectedTrack);
        this.Summary = PlaylistsViewModel.SummaryOf(rows.Count, rows.Count(r => r.Status == DownloadStatus.Done));
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    [RelayCommand]
    private void Back() => navigation.Back();

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

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (await playlists.RenameWithPromptAsync(playlistId, this.Name))
            await this.LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (await playlists.DeleteWithConfirmAsync(playlistId, this.Name))
            navigation.Back();
    }

    [RelayCommand]
    private async Task RemoveTrackAsync(PlaylistEntryViewModel entry)
    {
        await repository.RemoveTrackAsync(playlistId, entry.Row.Id);
        await this.ChangedAsync();
    }

    /// <summary>Moves a track to another place (drag and drop or Alt+Up/Down).</summary>
    public async Task MoveAsync(PlaylistEntryViewModel entry, int newIndex)
    {
        if (newIndex == entry.Position || newIndex < 0 || newIndex >= this.Entries.Count)
            return;
        await repository.MoveTrackAsync(playlistId, entry.Row.Id, newIndex);
        this.SelectedEntry = entry;
        await this.ChangedAsync();
    }

    [RelayCommand]
    private Task MoveUpAsync() => this.SelectedEntry is { } entry ? this.MoveAsync(entry, entry.Position - 1) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDownAsync() => this.SelectedEntry is { } entry ? this.MoveAsync(entry, entry.Position + 1) : Task.CompletedTask;

    private async Task ChangedAsync()
    {
        await this.LoadAsync();
        await playlists.ChangedAsync();
    }
}
