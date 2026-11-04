using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.Core.Models;

namespace Tapedeck.ViewModels;

/// <summary>
/// One editable metadata value with its origin. Typing a different value makes it the user's own;
/// typing the original value back or clearing the field drops the correction.
/// </summary>
public partial class MetadataFieldViewModel(string label, Action changed) : ViewModelBase
{
    private string original = string.Empty;
    private FieldSource originalSource;
    private bool loading;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserValue))]
    public partial FieldSource Source { get; private set; }

    public bool IsUserValue => this.Source == FieldSource.User;

    /// <summary>Value to store as correction, or <c>null</c> when the best available value should apply.</summary>
    public string? UserValue => this.IsUserValue && this.Text.Trim().Length > 0 ? this.Text.Trim() : null;

    /// <summary>Shows a value from the database without counting it as an edit.</summary>
    public void Load(string? value, FieldSource source)
    {
        this.loading = true;
        this.original = value ?? string.Empty;
        this.originalSource = source;
        this.Text = this.original;
        this.Source = source;
        this.loading = false;
    }

    partial void OnTextChanged(string value)
    {
        if (loading)
            return;
        Source = value == original ? originalSource : FieldSource.User;
        changed();
    }
}
