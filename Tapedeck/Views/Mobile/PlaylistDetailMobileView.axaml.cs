using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Mobile;

public partial class PlaylistDetailMobileView : UserControl
{
    public PlaylistDetailMobileView()
    {
        this.InitializeComponent();
    }

    /// <summary>Back from a track's page: the track last shown there is scrolled into view, as on the desktop.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.DataContext is PlaylistDetailViewModel { SelectedEntry: { } selected })
            Dispatcher.UIThread.Post(() => this.EntryList.ScrollIntoView(selected), DispatcherPriority.Loaded);
    }
}
