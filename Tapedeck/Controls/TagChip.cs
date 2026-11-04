using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Tapedeck.Resources;

namespace Tapedeck.Controls;

/// <summary>A removable chip, e.g. a genre: the text and a round remove button that runs <see cref="RemoveCommand"/>.</summary>
public sealed class TagChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TagChip, string?>(nameof(Text));

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<TagChip, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<object?> RemoveCommandParameterProperty =
        AvaloniaProperty.Register<TagChip, object?>(nameof(RemoveCommandParameter));

    /// <summary>Accessible name of the remove button, e.g. "Remove Disco".</summary>
    public static readonly DirectProperty<TagChip, string> RemoveLabelProperty =
        AvaloniaProperty.RegisterDirect<TagChip, string>(nameof(RemoveLabel), o => o.RemoveLabel);

    private string removeLabel = string.Empty;

    public string? Text
    {
        get => this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => this.GetValue(RemoveCommandProperty);
        set => this.SetValue(RemoveCommandProperty, value);
    }

    public object? RemoveCommandParameter
    {
        get => this.GetValue(RemoveCommandParameterProperty);
        set => this.SetValue(RemoveCommandParameterProperty, value);
    }

    public string RemoveLabel
    {
        get => this.removeLabel;
        private set => this.SetAndRaise(RemoveLabelProperty, ref this.removeLabel, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            this.RemoveLabel = string.Format(Strings.Genre_Remove, this.Text);
    }
}
