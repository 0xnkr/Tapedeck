using System;
using Avalonia.Controls;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class TrackDetailView : UserControl
{
    public TrackDetailView()
    {
        this.InitializeComponent();
    }

    // playlists may have been created since the menu was last open
    private void OnPlaylistMenuOpening(object? sender, EventArgs e) =>
        (this.DataContext as TrackDetailViewModel)?.Row.RefreshPlaylistMenu();
}
