using System.Globalization;

using Avalonia.Data.Converters;

namespace D2Compare.Controls;

public class IntToBoolConverter : IValueConverter
{
    public static readonly IntToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
