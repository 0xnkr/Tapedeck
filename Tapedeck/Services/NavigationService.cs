using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.ViewModels;

namespace Tapedeck.Services;

/// <summary>The page shown in the main window, with a back stack for sub pages such as the track details.</summary>
public sealed partial class NavigationService : ObservableObject
{
    private readonly Stack<ViewModelBase> _back = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    public partial ViewModelBase? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    /// <summary>Opens a sub page; <see cref="Back"/> returns to the current one.</summary>
    public void Open(ViewModelBase page)
    {
        if (Current is not null)
            _back.Push(Current);
        Current = page;
    }

    public void Back()
    {
        if (_back.TryPop(out var page))
            Current = page;
    }

    /// <summary>Shows a top level page from the navigation rail and forgets the back stack.</summary>
    public void Reset(ViewModelBase page)
    {
        _back.Clear();
        Current = page;
    }
}
