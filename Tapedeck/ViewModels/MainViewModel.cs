using System;
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
        NavigationService navigation, ActivityService activity, DialogService dialogs, PlayerService player)
    {
        this.Player = player;
        this.Playlists = playlists;
        this.Dialogs = dialogs;
        this.Library = library;
        this.Settings = settings;
        this.Navigation = navigation;
        this.Activity = activity;
        this.Navigation.Reset(this.Library);
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

    /// <summary>Escape and the mouse back button: close an open dialog first, else go back.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!this.Dialogs.CancelCurrent())
            this.Navigation.Back();
    }
}
