using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace fflux.UI.Shared.Converters;

/// <summary>
/// bool이 false이면 Visible, true이면 Collapsed로 변환합니다.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
