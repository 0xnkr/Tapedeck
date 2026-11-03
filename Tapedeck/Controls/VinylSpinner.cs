using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Tapedeck.Controls;

/// <summary>
/// A small vinyl record with the accent as its label. Spins while <see cref="IsSpinning"/> is set,
/// used as the busy indicator of the activity toast. Drawing and animation live in the ControlTheme.
/// </summary>
[PseudoClasses(Spinning)]
public sealed class VinylSpinner : TemplatedControl
{
    private const string Spinning = ":spinning";

    public static readonly StyledProperty<bool> IsSpinningProperty =
        AvaloniaProperty.Register<VinylSpinner, bool>(nameof(IsSpinning));

    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSpinningProperty)
            PseudoClasses.Set(Spinning, IsSpinning);
    }
}
