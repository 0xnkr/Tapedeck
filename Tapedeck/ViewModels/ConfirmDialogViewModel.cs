using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tapedeck.ViewModels;

/// <summary>What a confirmation asks; <see cref="OptionText"/> adds a check box, e.g. "also delete the file".</summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmText,
    string? OptionText = null,
    bool OptionDefault = false,
    bool IsDestructive = false);

/// <summary>The answer to a confirmed dialog; <see cref="Option"/> is the state of the check box.</summary>
public sealed record ConfirmResult(bool Option);

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

    /// <summary>Completes with the result, or <c>null</c> when cancelled.</summary>
    public Task<ConfirmResult?> Answer => this.answer.Task;

    [RelayCommand]
    private void Confirm() => this.answer.TrySetResult(new ConfirmResult(this.Option));

    [RelayCommand]
    private void Cancel() => this.answer.TrySetResult(null);

    void IDialog.Cancel() => this.Cancel();
}
