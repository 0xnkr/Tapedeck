using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.ViewModels;

/// <summary>
/// The license of one open source project as overlay in the main window. The text is embedded in the app and read
/// when the dialog opens, so it works offline.
/// </summary>
public partial class LicenseDialogViewModel(Credit credit, IPlatformService platform, ActivityService activity) : ViewModelBase, IDialog
{
    private readonly TaskCompletionSource<object?> closed = new();

    public string Title => string.Format(Strings.License_Title, credit.Name, credit.License);

    public string? Copyright => credit.Copyright;

    /// <summary>The license text; <c>null</c> while it is read.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    public partial string? Text { get; private set; }

    /// <summary>The text with each paragraph in one line, so it wraps to any width; copied is the original.</summary>
    [ObservableProperty]
    public partial string? DisplayText { get; private set; }

    /// <summary>Completes when the dialog is closed.</summary>
    public Task<object?> Closed => this.closed.Task;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            this.Text = await Credits.ReadLicenseAsync(credit.LicenseText);
        }
        catch (IOException ex)
        {
            this.Text = string.Format(Strings.License_ReadFailed, ex.Message);
        }
        this.DisplayText = LicenseText.Reflow(this.Text);
    }

    /// <summary>Copies the copyright notice and the license text, e.g. to pass them on with the app.</summary>
    [RelayCommand(CanExecute = nameof(HasText))]
    private async Task CopyAsync()
    {
        var text = this.Copyright is null ? this.Text! : this.Copyright + Environment.NewLine + Environment.NewLine + this.Text;
        await platform.SetClipboardTextAsync(text);
        activity.Confirm(Strings.Confirm_LicenseCopied);
    }

    private bool HasText() => this.Text is not null;

    [RelayCommand]
    private void Close() => this.closed.TrySetResult(null);

    void IDialog.Cancel() => this.Close();
}
