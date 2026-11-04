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
    public MainViewModel(LibraryViewModel library, NavigationService navigation, ActivityService activity)
    {
        Library = library;
        Navigation = navigation;
        Activity = activity;
        Navigation.Reset(Library);
    }

    public LibraryViewModel Library { get; }

    public NavigationService Navigation { get; }

    public ActivityService Activity { get; }

    [ObservableProperty]
    public partial AppPage CurrentPage { get; private set; } = AppPage.Library;

    /// <summary>Rail click: shows the page, also when it is already selected but a sub page is open.</summary>
    [RelayCommand]
    private void ShowPage(AppPage page)
    {
        CurrentPage = page;
        Navigation.Reset(page switch
        {
            AppPage.Library => Library,
            // Playlists and Settings are disabled in the rail until they exist
            _ => throw new NotSupportedException($"Page {page} is not implemented yet."),
        });
    }

    [RelayCommand]
    private void Back() => Navigation.Back();
}
