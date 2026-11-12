using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TabDock.Converters;

/// <summary>Keeps inline capture actions inside the space below the workspace chrome.</summary>
public sealed class RemainingHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not double height || !double.IsFinite(height))
            return 0d;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] is double occupied && double.IsFinite(occupied))
                height -= occupied;
        }
        double maximum = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double cap)
            && double.IsFinite(cap) ? Math.Max(0, cap) : double.MaxValue;
        return Math.Clamp(height, 0, maximum);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
