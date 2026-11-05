using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.ViewModels;

namespace Tapedeck.Services;

/// <summary>Shows confirmations and text prompts as overlay in the main window, one at a time.</summary>
public sealed partial class DialogService : ObservableObject
{
    /// <summary>The open dialog (<see cref="ConfirmDialogViewModel"/>, <see cref="PromptDialogViewModel"/> or <see cref="LicenseDialogViewModel"/>), or <c>null</c>.</summary>
    [ObservableProperty]
    public partial ViewModelBase? Current { get; private set; }

    /// <returns>The answer, or <c>null</c> when cancelled or another dialog is already open.</returns>
    public Task<ConfirmResult?> ConfirmAsync(ConfirmRequest request)
    {
        var dialog = new ConfirmDialogViewModel(request);
        return this.ShowAsync(dialog, dialog.Answer);
    }

    /// <returns>The entered text, or <c>null</c> when cancelled or another dialog is already open.</returns>
    public Task<string?> PromptAsync(PromptRequest request)
    {
        var dialog = new PromptDialogViewModel(request);
        return this.ShowAsync(dialog, dialog.Answer);
    }

    /// <summary>Shows a license text and completes when the dialog is closed.</summary>
    public Task ShowLicenseAsync(LicenseDialogViewModel dialog)
    {
        dialog.LoadCommand.Execute(null);
        return this.ShowAsync(dialog, dialog.Closed);
    }

    /// <summary>Cancels the open dialog, e.g. on Escape.</summary>
    /// <returns><c>true</c> when a dialog was open.</returns>
    public bool CancelCurrent()
    {
        if (this.Current is not IDialog dialog)
            return false;
        dialog.Cancel();
        return true;
    }

    private async Task<T?> ShowAsync<T>(ViewModelBase dialog, Task<T?> answer) where T : class
    {
        if (this.Current is not null)
            return null;

        this.Current = dialog;
        try
        {
            return await answer;
        }
        finally
        {
            this.Current = null;
        }
    }
}
