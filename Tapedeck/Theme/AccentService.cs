using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Tapedeck.Theme;

/// <summary>Recolors the app at runtime from a single accent color.</summary>
public interface IAccentService
{
    Color Accent { get; }

    event EventHandler? AccentChanged;

    void Apply(Color accent);
}

/// <summary>
/// Derives all accent resources from one color and writes them into the application resources.
/// Every control references them with DynamicResource, so a call to <see cref="Apply"/> recolors the app live.
/// </summary>
public sealed class AccentService : IAccentService
{
    // Alpha values from the design system: accent-soft is accent + 0x29, the "user value" outline accent + 0x66
    private const byte SoftAlpha = 0x29;
    private const byte OutlineAlpha = 0x66;

    private readonly IResourceDictionary _resources;
    private readonly Color _darkOnAccent;
    private readonly Color _lightOnAccent;

    public AccentService(IResourceDictionary resources)
    {
        _resources = resources;
        _darkOnAccent = Lookup("Color.OnAccent");
        _lightOnAccent = Lookup("Color.Text");
        Apply(Lookup("Color.Accent"));
    }

    public Color Accent { get; private set; }

    public event EventHandler? AccentChanged;

    public void Apply(Color accent)
    {
        Accent = accent;
        var onAccent = ColorMath.Contrast(accent, _darkOnAccent) >= ColorMath.Contrast(accent, _lightOnAccent)
            ? _darkOnAccent
            : _lightOnAccent;

        _resources["Color.Accent"] = accent;
        _resources["Brush.Accent"] = new SolidColorBrush(accent);
        _resources["Brush.AccentSoft"] = new SolidColorBrush(accent.WithAlpha(SoftAlpha));
        _resources["Brush.AccentOutline"] = new SolidColorBrush(accent.WithAlpha(OutlineAlpha));
        _resources["Brush.OnAccent"] = new SolidColorBrush(onAccent);

        // Fluent controls (focus, selection, sliders) follow the system accent keys
        _resources["SystemAccentColor"] = accent;
        for (var step = 1; step <= 3; step++)
        {
            _resources[$"SystemAccentColorDark{step}"] = accent.Mix(Colors.Black, 0.15 * step);
            _resources[$"SystemAccentColorLight{step}"] = accent.Mix(Colors.White, 0.15 * step);
        }

        AccentChanged?.Invoke(this, EventArgs.Empty);
    }

    private Color Lookup(string key) =>
        _resources.TryGetResource(key, ThemeVariant.Dark, out var value) && value is Color color
            ? color
            : throw new InvalidOperationException($"Color resource '{key}' is missing from the palette.");
}
