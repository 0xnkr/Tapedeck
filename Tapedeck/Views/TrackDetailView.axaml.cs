using System;
using Avalonia;
using Avalonia.Controls;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class TrackDetailView : UserControl
{
    public TrackDetailView()
    {
        this.InitializeComponent();
    }

    // the lyrics follow the player only while the page is shown; a page left behind lets go of it
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (this.DataContext as TrackDetailViewModel)?.LyricsPanel.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (this.DataContext as TrackDetailViewModel)?.LyricsPanel.Deactivate();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TopLevel.GetTopLevel(this) is not null)
            (this.DataContext as TrackDetailViewModel)?.LyricsPanel.Activate();
    }

    // playlists may have been created since the menu was last open
    private void OnPlaylistMenuOpening(object? sender, EventArgs e) =>
        (this.DataContext as TrackDetailViewModel)?.Row.RefreshPlaylistMenu();
}
