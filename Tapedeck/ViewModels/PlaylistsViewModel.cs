using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;
using Tapedeck.Core.Services;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Creates the page of one playlist.</summary>
public delegate PlaylistDetailViewModel PlaylistDetailFactory(long playlistId);

/// <summary>One playlist on the overview and in the "add to playlist" menus.</summary>
public partial class PlaylistItemViewModel(Playlist playlist, int downloaded, Action<PlaylistItemViewModel> open) : ViewModelBase
{
    public long Id { get; } = playlist.Id;

    public string Name { get; } = playlist.Name;

    /// <summary>"20 tracks · 18 downloaded"; missing files do not count as downloaded.</summary>
    public string Summary { get; } = PlaylistsViewModel.SummaryOf(playlist.TrackCount, downloaded, playlist.SourceUrl is not null);

    public string OpenLabel => string.Format(Strings.Playlists_Open, this.Name);

    [RelayCommand]
    private void Open() => open(this);
}

/// <summary>
/// The playlists page, and the one place that creates, renames and deletes playlists, so names stay unique
/// and the .m3u8 files follow every change.
/// </summary>
public partial class PlaylistsViewModel(
    IPlaylistRepository repository,
    LibraryFiles files,
    DialogService dialogs,
    ActivityService activity,
    NavigationService navigation,
    PlaylistSync sync,
    ToolSetup toolSetup,
    MetadataService metadata,
    IPlatformService platform,
    PlaylistDetailFactory createDetail) : ViewModelBase
{
    /// <summary>All playlists by name; shared with the "add to playlist" menus.</summary>
    public ObservableCollection<PlaylistItemViewModel> Playlists { get; } = [];

    public bool IsEmpty => this.Playlists.Count == 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var items = new List<PlaylistItemViewModel>();
        foreach (var playlist in await repository.ListAsync())
        {
            var tracks = await repository.GetTracksAsync(playlist.Id);
            var downloaded = tracks.Count(t => files.StatusOf(t) == DownloadStatus.Done);
            items.Add(new PlaylistItemViewModel(playlist, downloaded, this.OpenPlaylist));
        }

        this.Playlists.Clear();
        foreach (var item in items)
            this.Playlists.Add(item);
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    [RelayCommand]
    private Task CreateAsync() => this.CreateWithPromptAsync();

    /// <summary>Asks for the link of a YouTube playlist; the dialog stays open with the reason until it can be read.</summary>
    [RelayCommand]
    private async Task ImportFromLinkAsync()
    {
        // offer the clipboard when it holds a playlist link, the usual way to get here
        var clipboard = (await platform.GetClipboardTextAsync())?.Trim();
        var initial = PlaylistLink.TryParse(clipboard, out _) ? clipboard! : string.Empty;

        YtPlaylist? source = null;
        var link = await dialogs.PromptAsync(new PromptRequest(
            Strings.Prompt_ImportPlaylistTitle, Strings.Prompt_PlaylistLink, Strings.Prompt_Import, initial,
            async text =>
            {
                if (!PlaylistLink.TryParse(text, out var listId))
                    return PlaylistLink.IsMixLink(text) ? Strings.Prompt_MixLink : Strings.Prompt_NotAPlaylistLink;
                try
                {
                    source = await this.FetchPlaylistAsync(listId);
                    return null;
                }
                catch (Exception ex)
                {
                    return string.Format(Strings.Notice_PlaylistReadFailed, ex.Message);
                }
            }));
        if (link is not null && source is not null)
            await this.ImportAsync(source);
    }

    /// <summary>Reads the videos of a YouTube playlist; installs the tools first if needed.</summary>
    public async Task<YtPlaylist> FetchPlaylistAsync(string listId)
    {
        await toolSetup.EnsureAsync();
        using (activity.Begin(Strings.Activity_ReadingPlaylist))
            return await metadata.FetchPlaylistAsync(listId);
    }

    /// <summary>
    /// Creates a playlist linked to the YouTube playlist, or takes the one imported before, opens it and adds the videos.
    /// </summary>
    public async Task ImportAsync(YtPlaylist source)
    {
        var url = PlaylistLink.Url(source.Id);
        var id = (await repository.FindBySourceAsync(url))?.Id
                 ?? await repository.CreateAsync(await this.FreeNameAsync(source.Title ?? Strings.Playlist_YoutubeName), url);
        await this.ChangedAsync();
        await this.Open(id).ImportAsync(source);
    }

    /// <summary>Asks for a name and creates the playlist.</summary>
    /// <returns>The new playlist, or <c>null</c> when cancelled.</returns>
    public async Task<PlaylistItemViewModel?> CreateWithPromptAsync()
    {
        var name = await dialogs.PromptAsync(new PromptRequest(
            Strings.Prompt_NewPlaylistTitle, Strings.Prompt_PlaylistName, Strings.Prompt_Create, string.Empty,
            text => this.ValidateNameAsync(text, exceptId: null)));
        if (name is null)
            return null;

        var id = await repository.CreateAsync(name);
        await this.ChangedAsync();
        return this.Playlists.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Asks for a new name; <c>true</c> when the playlist was renamed.</summary>
    public async Task<bool> RenameWithPromptAsync(long id, string currentName)
    {
        var name = await dialogs.PromptAsync(new PromptRequest(
            Strings.Prompt_RenamePlaylistTitle, Strings.Prompt_PlaylistName, Strings.Prompt_Rename, currentName,
            text => this.ValidateNameAsync(text, exceptId: id)));
        if (name is null || name == currentName)
            return false;

        await repository.RenameAsync(id, name);
        await this.ChangedAsync();
        return true;
    }

    /// <summary>Asks for confirmation and deletes the playlist; its tracks stay in the library.</summary>
    public async Task<bool> DeleteWithConfirmAsync(long id, string name)
    {
        var answer = await dialogs.ConfirmAsync(new ConfirmRequest(
            string.Format(Strings.Playlist_DeleteTitle, name), Strings.Playlist_DeleteMessage, Strings.Playlist_Delete,
            IsDestructive: true));
        if (answer is null)
            return false;

        await repository.DeleteAsync(id);
        await this.ChangedAsync();
        activity.Confirm(string.Format(Strings.Confirm_PlaylistDeleted, name));
        return true;
    }

    /// <summary>Adds a track and reports it in the toast, also when it was already in the playlist.</summary>
    public async Task AddTrackAsync(long playlistId, long trackId, string trackTitle)
    {
        var added = await repository.AddTracksAsync(playlistId, [trackId]);
        var name = this.Playlists.FirstOrDefault(p => p.Id == playlistId)?.Name ?? string.Empty;
        await this.ChangedAsync();
        activity.Confirm(string.Format(added > 0 ? Strings.Confirm_AddedToPlaylist : Strings.Confirm_AlreadyInPlaylist,
            trackTitle, name));
    }

    /// <summary>Reloads the list and asks for the .m3u8 files to be written.</summary>
    public async Task ChangedAsync()
    {
        await this.LoadAsync();
        sync.Request();
    }

    public static string SummaryOf(int trackCount, int doneCount, bool isLinked)
    {
        var count = string.Format(trackCount == 1 ? Strings.Library_TrackCount_One : Strings.Library_TrackCount_Other, trackCount);
        var summary = string.Format(Strings.Library_Summary, count, doneCount);
        return isLinked ? string.Format(Strings.Playlist_Linked, summary) : summary;
    }

    private void OpenPlaylist(PlaylistItemViewModel playlist) => this.Open(playlist.Id);

    private PlaylistDetailViewModel Open(long id)
    {
        var detail = createDetail(id);
        navigation.Open(detail);
        detail.LoadCommand.Execute(null);
        return detail;
    }

    /// <summary>The wanted name, or "Name (2)", "Name (3)", … when it is taken.</summary>
    private async Task<string> FreeNameAsync(string wanted)
    {
        var baseName = wanted.Trim().Length > 0 ? wanted.Trim() : Strings.Playlist_YoutubeName;
        var name = baseName;
        for (var i = 2; await this.ValidateNameAsync(name, exceptId: null) is not null; i++)
            name = string.Format(Strings.Playlist_NumberedName, baseName, i);
        return name;
    }

    /// <summary>The name must be set, unused and must not map to another playlist's .m3u8 file name.</summary>
    private async Task<string?> ValidateNameAsync(string name, long? exceptId)
    {
        if (name.Length == 0)
            return Strings.Prompt_NameEmpty;
        if (await repository.NameExistsAsync(name, exceptId))
            return string.Format(Strings.Prompt_NameTaken, name);

        var fileName = M3uWriter.FileNameFor(name);
        IEnumerable<Playlist> others = (await repository.ListAsync()).Where(p => p.Id != exceptId);
        var sameFile = others.FirstOrDefault(p =>
            string.Equals(M3uWriter.FileNameFor(p.Name), fileName, StringComparison.OrdinalIgnoreCase));
        return sameFile is null ? null : string.Format(Strings.Prompt_FileNameTaken, name, sameFile.Name);
    }
}
