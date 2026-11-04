using Avalonia.Controls;
using Avalonia.Input;

namespace Tapedeck.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        this.InitializeComponent();
        // Enter confirms, Escape cancels (window key binding); focus the action so the keyboard works right away
        this.AttachedToVisualTree += (_, _) => this.ConfirmButton.Focus(NavigationMethod.Tab);
    }
}
