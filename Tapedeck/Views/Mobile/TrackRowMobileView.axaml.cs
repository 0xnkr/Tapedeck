using System;
using Avalonia.Controls;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Mobile;

public partial class TrackRowMobileView : UserControl
{
    public TrackRowMobileView()
    {
        this.InitializeComponent();
    }

    // playlists may have been created since the menu was last open
    private void OnMenuOpening(object? sender, EventArgs e) => (this.DataContext as TrackRowViewModel)?.RefreshPlaylistMenu();
}
