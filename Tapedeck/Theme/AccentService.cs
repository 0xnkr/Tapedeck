using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Tapedeck.Theme;

/// <summary>Recolors the app at runtime from a single accent color.</summary>
public interface IAccentService
{
    Color Accent { get; }

    /// <summary>Current accent as <c>#RRGGBB</c>, for view models that do not know Avalonia types.</summary>
    string AccentHex { get; }

    /// <summary>The palette's accent and its alternatives, as <c>#RRGGBB</c>.</summary>
    IReadOnlyList<string> PresetHexes { get; }

    event EventHandler? AccentChanged;

    void Apply(Color accent);

    /// <summary>Applies a hex color such as <c>#C8B6FF</c>; returns <c>false</c> and changes nothing when it is not one.</summary>
    bool TryApply(string hex);
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

        var paletteAccent = this.Lookup("Color.Accent");
        this.PresetHexes =
        [
            ToHex(paletteAccent),
            ToHex(this.Lookup("Color.AccentAlternative1")),
            ToHex(this.Lookup("Color.AccentAlternative2")),
            ToHex(this.Lookup("Color.AccentAlternative3")),
        ];
        this.Apply(paletteAccent);
    }

    public Color Accent { get; private set; }

    public string AccentHex => ToHex(this.Accent);

    public IReadOnlyList<string> PresetHexes { get; }

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

        this.AccentChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryApply(string hex)
    {
        // only opaque #RRGGBB: an accent with alpha would make the derived tints unpredictable
        var text = hex.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (text.Length != 7 || !Color.TryParse(text, out var color))
            return false;
        this.Apply(color);
        return true;
    }

    private static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    private Color Lookup(string key) =>
        this.resources.TryGetResource(key, ThemeVariant.Dark, out var value) && value is Color color
            ? color
            : throw new InvalidOperationException($"Color resource '{key}' is missing from the palette.");
}
