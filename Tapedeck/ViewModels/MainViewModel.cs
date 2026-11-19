using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>Pages reachable from the navigation rail.</summary>
public enum AppPage
{
    Library,
    Playlists,
    Settings,
}

/// <summary>Shell of the main window: navigation rail, current page and the activity toast.</summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(LibraryViewModel library, PlaylistsViewModel playlists, SettingsViewModel settings,
        NavigationService navigation, ActivityService activity, DialogService dialogs, PlayerService player,
        SpotifyImportViewModel spotifyImport)
    {
        this.SpotifyImport = spotifyImport;
        this.Player = player;
        this.Playlists = playlists;
        this.Dialogs = dialogs;
        this.Library = library;
        this.Settings = settings;
        this.Navigation = navigation;
        this.Activity = activity;
        this.Navigation.Reset(this.Library);
        this.Navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationService.Current))
                this.OnPropertyChanged(nameof(this.IsSpotifyImportShown));
        };
    }

    /// <summary>For the reminder above every page while found Spotify songs wait to be added.</summary>
    public SpotifyImportViewModel SpotifyImport { get; }

    /// <summary>The reminder offers "Add N songs" there and a way to the page everywhere else.</summary>
    public bool IsSpotifyImportShown => this.Navigation.Current == this.SpotifyImport;

    [RelayCommand]
    private void OpenSpotifyImport()
    {
        this.ShowPage(AppPage.Playlists);
        this.Navigation.Open(this.SpotifyImport);
    }

    public LibraryViewModel Library { get; }

    public PlaylistsViewModel Playlists { get; }

    public SettingsViewModel Settings { get; }

    public NavigationService Navigation { get; }

    public ActivityService Activity { get; }

    public DialogService Dialogs { get; }

    /// <summary>The track that plays, for the player bar.</summary>
    public PlayerService Player { get; }

    /// <summary>The player bar's title opens the playing track's details.</summary>
    [RelayCommand]
    private void OpenPlaying()
    {
        if (this.Player.CurrentTrackId is { } id && this.Library.RowFor(id) is { } row)
            row.OpenDetailsCommand.Execute(null);
    }

    /// <summary>Space outside of text fields.</summary>
    public void TogglePlayback() => this.Player.TogglePauseCommand.Execute(null);

    [ObservableProperty]
    public partial AppPage CurrentPage { get; private set; } = AppPage.Library;

    /// <summary>Rail click: shows the page, also when it is already selected but a sub page is open.</summary>
    [RelayCommand]
    private void ShowPage(AppPage page)
    {
        this.CurrentPage = page;
        this.Navigation.Reset(page switch
        {
            AppPage.Library => this.Library,
            AppPage.Playlists => this.Playlists,
            _ => this.Settings,
        });
        if (page == AppPage.Settings)
            this.Settings.LoadCommand.Execute(null);
        else if (page == AppPage.Playlists)
            this.Playlists.LoadCommand.Execute(null);
        else if (page == AppPage.Library)
            this.Library.RecheckFiles();
    }

    /// <summary>
    /// A link shared with Tapedeck, e.g. from the YouTube app: shows the library with the link in its link row and adds
    /// it, as if it had been pasted there. Share texts may carry a title before the link.
    /// </summary>
    public void AddSharedLink(string text)
    {
        // null splits at any whitespace
        var link = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => word.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? text.Trim();
        this.ShowPage(AppPage.Library);
        this.Library.NewLink = link;
        if (this.Library.AddLinkCommand.CanExecute(null))
            this.Library.AddLinkCommand.Execute(null);
    }

    /// <summary>
    /// The phone's back gesture: closes a dialog, goes back to the list a page came from, else to the library.
    /// </summary>
    /// <returns><c>false</c> on the library itself, so the system may leave the app.</returns>
    public bool TryGoBack()
    {
        if (this.Dialogs.CancelCurrent())
            return true;
        if (this.Navigation.CanGoBack)
        {
            this.Navigation.Back();
            return true;
        }
        if (this.CurrentPage == AppPage.Library)
            return false;
        this.ShowPage(AppPage.Library);
        return true;
    }

    /// <summary>Escape and the mouse back button: close an open dialog first, else go back.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!this.Dialogs.CancelCurrent())
            this.Navigation.Back();
    }
}
