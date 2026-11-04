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
}
