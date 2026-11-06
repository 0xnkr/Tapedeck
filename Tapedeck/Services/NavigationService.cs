using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.ViewModels;

namespace Tapedeck.Services;

/// <summary>The page shown in the main window, with a back stack for sub pages such as the track details.</summary>
public sealed partial class NavigationService : ObservableObject
{
    private readonly Stack<ViewModelBase> back = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    public partial ViewModelBase? Current { get; private set; }

    public bool CanGoBack => this.back.Count > 0;

    /// <summary>Opens a sub page; <see cref="Back"/> returns to the current one.</summary>
    public void Open(ViewModelBase page)
    {
        if (this.Current is not null)
            this.back.Push(this.Current);
        this.Current = page;
    }

    /// <summary>Swaps the current sub page for another, e.g. the next track's details; <see cref="Back"/> skips both.</summary>
    public void Replace(ViewModelBase page) => this.Current = page;

    public void Back()
    {
        if (this.back.TryPop(out var page))
            this.Current = page;
    }

    /// <summary>Shows a top level page from the navigation rail and forgets the back stack.</summary>
    public void Reset(ViewModelBase page)
    {
        this.back.Clear();
        this.Current = page;
    }
}
