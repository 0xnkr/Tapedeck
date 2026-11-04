using System;
using Avalonia.Controls;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Library;

public partial class TrackRowView : UserControl
{
    public TrackRowView()
    {
        this.InitializeComponent();
        this.DoubleTapped += (_, _) => (this.DataContext as TrackRowViewModel)?.OpenDetailsCommand.Execute(null);
    }

    // playlists may have been created since the menu was last open
    private void OnMenuOpening(object? sender, EventArgs e) => (this.DataContext as TrackRowViewModel)?.RefreshPlaylistMenu();
}
