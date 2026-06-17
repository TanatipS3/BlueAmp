using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BaudioApp.Models;

namespace BaudioApp.Helpers;

/// Visible when bound value equals ConverterParameter string.
[ValueConversion(typeof(object), typeof(Visibility))]
public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// Collapsed when bound value equals ConverterParameter string; Visible otherwise.
[ValueConversion(typeof(object), typeof(Visibility))]
public class InequalityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() != parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// Visible when bool is true.
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// Maps DeviceConnectionState to a hex color string for status dot fill.
[ValueConversion(typeof(DeviceConnectionState), typeof(string))]
public class StateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DeviceConnectionState s ? s switch
        {
            DeviceConnectionState.Connected  => "#00C853",
            DeviceConnectionState.Connecting => "#FF8C00",
            DeviceConnectionState.Error      => "#FF5252",
            _                                => "#555555"
        } : "#555555";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// True when DeviceConnectionState equals ConverterParameter string.
[ValueConversion(typeof(DeviceConnectionState), typeof(bool))]
public class StateEqualityToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// Scales AudioPeak (0–1) by the container's ActualWidth.
/// MultiBinding: [0] = float/double peak, [1] = double containerWidth
public class PeakToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0.0;
        double peak  = values[0] is double d0 ? d0 : values[0] is float f0 ? f0 : 0;
        double width = values[1] is double d1 ? d1 : values[1] is float f1 ? f1 : 0;
        return Math.Max(0, peak * width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing, Binding.DoNothing];
}

/// Converts a peak value (double 0–1) to a waveform bar height (2–28 px).
[ValueConversion(typeof(double), typeof(double))]
public class PeakToBarHeightConverter : IValueConverter
{
    private const double MaxHeight = 28;
    private const double MinHeight = 2;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double peak = value is double d ? d : value is float f ? f : 0;
        return Math.Max(MinHeight, peak * MaxHeight);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
