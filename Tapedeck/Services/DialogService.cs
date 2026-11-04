using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.ViewModels;

namespace Tapedeck.Services;

/// <summary>Shows confirmations as overlay in the main window, one at a time.</summary>
public sealed partial class DialogService : ObservableObject
{
    /// <summary>The open dialog, or <c>null</c>.</summary>
    [ObservableProperty]
    public partial ConfirmDialogViewModel? Current { get; private set; }

    /// <returns>The answer, or <c>null</c> when cancelled or another dialog is already open.</returns>
    public async Task<ConfirmResult?> ConfirmAsync(ConfirmRequest request)
    {
        if (this.Current is not null)
            return null;

        var dialog = new ConfirmDialogViewModel(request);
        this.Current = dialog;
        try
        {
            return await dialog.Answer;
        }
        finally
        {
            this.Current = null;
        }
    }

    /// <summary>Cancels the open dialog, e.g. on Escape.</summary>
    /// <returns><c>true</c> when a dialog was open.</returns>
    public bool CancelCurrent()
    {
        if (this.Current is not { } dialog)
            return false;
        dialog.CancelCommand.Execute(null);
        return true;
    }
}
