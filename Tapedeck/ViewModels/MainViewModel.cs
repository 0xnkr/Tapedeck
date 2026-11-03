using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tapedeck.ViewModels;

/// <summary>Pages reachable from the navigation rail.</summary>
public enum AppPage
{
    Library,
    Playlists,
    Settings,
}

/// <summary>Shell of the main window: which page the navigation rail shows.</summary>
public partial class MainViewModel(LibraryViewModel library) : ViewModelBase
{
    public LibraryViewModel Library { get; } = library;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentView))]
    public partial AppPage CurrentPage { get; set; } = AppPage.Library;

    public ViewModelBase CurrentView => CurrentPage switch
    {
        AppPage.Library => Library,
        // Playlists and Settings are disabled in the rail until they exist
        _ => throw new NotSupportedException($"Page {CurrentPage} is not implemented yet."),
    };
}
