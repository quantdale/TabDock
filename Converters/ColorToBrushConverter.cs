using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TabDock.Converters;

[ValueConversion(typeof(string), typeof(Brush))]
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#2196F3"));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
