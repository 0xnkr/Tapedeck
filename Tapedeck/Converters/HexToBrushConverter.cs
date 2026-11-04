using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Tapedeck.Converters;

/// <summary>Turns a <c>#RRGGBB</c> string from a view model into a brush, e.g. for accent swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter INSTANCE = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
