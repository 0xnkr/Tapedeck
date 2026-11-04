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
    private const byte SOFT_ALPHA = 0x29;
    private const byte OUTLINE_ALPHA = 0x66;

    private readonly IResourceDictionary resources;
    private readonly Color darkOnAccent;
    private readonly Color lightOnAccent;

    public AccentService(IResourceDictionary resources)
    {
        this.resources = resources;
        this.darkOnAccent = this.Lookup("Color.OnAccent");
        this.lightOnAccent = this.Lookup("Color.Text");
        this.Apply(this.Lookup("Color.Accent"));
    }

    public Color Accent { get; private set; }

    public event EventHandler? AccentChanged;

    public void Apply(Color accent)
    {
        this.Accent = accent;
        var onAccent = ColorMath.Contrast(accent, this.darkOnAccent) >= ColorMath.Contrast(accent, this.lightOnAccent)
            ? this.darkOnAccent
            : this.lightOnAccent;

        this.resources["Color.Accent"] = accent;
        this.resources["Brush.Accent"] = new SolidColorBrush(accent);
        this.resources["Brush.AccentSoft"] = new SolidColorBrush(accent.WithAlpha(SOFT_ALPHA));
        this.resources["Brush.AccentOutline"] = new SolidColorBrush(accent.WithAlpha(OUTLINE_ALPHA));
        this.resources["Brush.OnAccent"] = new SolidColorBrush(onAccent);

        // Fluent controls (focus, selection, sliders) follow the system accent keys
        this.resources["SystemAccentColor"] = accent;
        for (var step = 1; step <= 3; step++)
        {
            this.resources[$"SystemAccentColorDark{step}"] = accent.Mix(Colors.Black, 0.15 * step);
            this.resources[$"SystemAccentColorLight{step}"] = accent.Mix(Colors.White, 0.15 * step);
        }

        AccentChanged?.Invoke(this, EventArgs.Empty);
    }

    private Color Lookup(string key) =>
        this.resources.TryGetResource(key, ThemeVariant.Dark, out var value) && value is Color color
            ? color
            : throw new InvalidOperationException($"Color resource '{key}' is missing from the palette.");
}
