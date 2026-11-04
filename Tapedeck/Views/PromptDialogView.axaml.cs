using Avalonia.Controls;
using Avalonia.Input;

namespace Tapedeck.Views;

public partial class PromptDialogView : UserControl
{
    public PromptDialogView()
    {
        this.InitializeComponent();
        // type right away; a prefilled name (rename) is selected so typing replaces it
        this.AttachedToVisualTree += (_, _) =>
        {
            this.InputBox.Focus(NavigationMethod.Tab);
            this.InputBox.SelectAll();
        };
    }
}
