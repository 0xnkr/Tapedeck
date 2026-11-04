using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Tapedeck.Converters;

/// <summary>
/// Loads a local thumbnail file, decoded at the size the view needs.
/// The optional converter parameter is the decode width in pixels (default: table size).
/// </summary>
public sealed class ThumbnailConverter : IValueConverter
{
    public static readonly ThumbnailConverter Instance = new();

    // Twice the 72 px cell width for sharp output on high-DPI screens
    private const int DecodeWidth = 144;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var width = parameter is string text && int.TryParse(text, out var w) ? w : DecodeWidth;
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // a broken or locked file just shows the placeholder
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
