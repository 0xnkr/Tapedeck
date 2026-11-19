using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Mobile;

public partial class LibraryMobileView : UserControl
{
    public LibraryMobileView()
    {
        this.InitializeComponent();
    }

    /// <summary>Back from a track's page: the track last shown there is scrolled into view, as on the desktop.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.DataContext is LibraryViewModel { SelectedTrack: { } selected })
            Dispatcher.UIThread.Post(() => this.TrackList.ScrollIntoView(selected), DispatcherPriority.Loaded);
    }
}
