using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Tapedeck.Converters;

/// <summary>
/// Binds a group of radio buttons to one value: checked when the value equals the parameter,
/// checking a button writes its parameter back.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
