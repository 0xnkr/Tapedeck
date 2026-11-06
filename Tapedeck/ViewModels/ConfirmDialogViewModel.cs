using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tapedeck.ViewModels;

/// <summary>
/// What a confirmation asks; <see cref="OptionText"/> adds a check box, e.g. "also delete the file", and
/// <see cref="OfferDontAsk"/> one for not asking again, which the caller has to remember.
/// </summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmText,
    string? OptionText = null,
    bool OptionDefault = false,
    bool IsDestructive = false,
    bool OfferDontAsk = false);

/// <summary>The answer to a confirmed dialog; <see cref="Option"/> is the state of the check box.</summary>
/// <param name="DontAskAgain">The user wants the same answer from now on without the dialog.</param>
public sealed record ConfirmResult(bool Option, bool DontAskAgain = false);

/// <summary>A dialog shown as overlay in the main window; Escape cancels it.</summary>
public interface IDialog
{
    void Cancel();
}

/// <summary>A confirmation shown as overlay in the main window until confirmed or cancelled.</summary>
public partial class ConfirmDialogViewModel(ConfirmRequest request) : ViewModelBase, IDialog
{
    private readonly TaskCompletionSource<ConfirmResult?> answer = new();

    public ConfirmRequest Request { get; } = request;

    public bool HasOption => this.Request.OptionText is not null;

    [ObservableProperty]
    public partial bool Option { get; set; } = request.OptionDefault;

    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    /// <summary>Completes with the result, or <c>null</c> when cancelled.</summary>
    public Task<ConfirmResult?> Answer => this.answer.Task;

    [RelayCommand]
    private void Confirm() => this.answer.TrySetResult(new ConfirmResult(this.Option, this.DontAskAgain));

    [RelayCommand]
    private void Cancel() => this.answer.TrySetResult(null);

    void IDialog.Cancel() => this.Cancel();
}
