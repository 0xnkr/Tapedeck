using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Tapedeck.Converters;

/// <summary>Loads a local thumbnail file, decoded at the small size the table needs.</summary>
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
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
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
