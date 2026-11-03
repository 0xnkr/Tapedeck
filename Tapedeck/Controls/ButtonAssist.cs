using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Tapedeck.Controls;

/// <summary>Attached properties for buttons and toggle buttons, e.g. a leading icon.</summary>
public sealed class ButtonAssist : AvaloniaObject
{
    /// <summary>Leading icon from <c>Icons.axaml</c>, drawn by the button's theme.</summary>
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<ButtonAssist, TemplatedControl, Geometry?>("Icon");

    public static Geometry? GetIcon(TemplatedControl element) => element.GetValue(IconProperty);

    public static void SetIcon(TemplatedControl element, Geometry? value) => element.SetValue(IconProperty, value);
}
