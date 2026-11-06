using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Back from the details: the view keeps its scroll position, and the track last shown there (possibly a later one,
    /// reached with the arrows) is scrolled into view.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.DataContext is LibraryViewModel { SelectedTrack: { } selected })
            Dispatcher.UIThread.Post(() => this.TrackList.ScrollIntoView(selected), DispatcherPriority.Loaded);
    }
}
