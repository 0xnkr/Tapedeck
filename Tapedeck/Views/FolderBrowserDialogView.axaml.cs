using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Tapedeck.Views;

public partial class FolderBrowserDialogView : UserControl
{
    public FolderBrowserDialogView()
    {
        this.InitializeComponent();
        // type the new folder's name right away once the field opens
        this.NamingRow.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && this.NamingRow.IsVisible)
                Dispatcher.UIThread.Post(() => this.NameBox.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        };
    }
}
