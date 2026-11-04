using Avalonia.Controls;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Library;

public partial class TrackRowView : UserControl
{
    public TrackRowView()
    {
        InitializeComponent();
        DoubleTapped += (_, _) => (DataContext as TrackRowViewModel)?.OpenDetailsCommand.Execute(null);
    }
}
