using Avalonia.Controls;
using Avalonia.Input;

namespace Tapedeck.Views;

public partial class LicenseDialogView : UserControl
{
    public LicenseDialogView()
    {
        this.InitializeComponent();
        // Enter and Escape close it (window key binding); focus the button so the keyboard works right away
        this.AttachedToVisualTree += (_, _) => this.CloseButton.Focus(NavigationMethod.Tab);
    }
}
