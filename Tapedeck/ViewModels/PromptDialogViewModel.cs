using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tapedeck.ViewModels;

/// <summary>
/// Asks for one line of text. <see cref="Validate"/> returns an error message to show in the dialog,
/// or <c>null</c> when the text is fine.
/// </summary>
public sealed record PromptRequest(
    string Title,
    string Label,
    string ConfirmText,
    string InitialText,
    Func<string, Task<string?>> Validate);

/// <summary>A text prompt shown as overlay in the main window; it stays open until the text is valid or cancelled.</summary>
public partial class PromptDialogViewModel(PromptRequest request) : ViewModelBase, IDialog
{
    private readonly TaskCompletionSource<string?> answer = new();

    public PromptRequest Request { get; } = request;

    [ObservableProperty]
    public partial string Text { get; set; } = request.InitialText;

    /// <summary>Why the text cannot be used; <c>null</c> while it is fine.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Completes with the trimmed text, or <c>null</c> when cancelled.</summary>
    public Task<string?> Answer => this.answer.Task;

    partial void OnTextChanged(string value) => this.Error = null;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var text = this.Text.Trim();
        this.Error = await this.Request.Validate(text);
        if (this.Error is null)
            this.answer.TrySetResult(text);
    }

    [RelayCommand]
    private void Cancel() => this.answer.TrySetResult(null);

    void IDialog.Cancel() => this.Cancel();
}
