using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace fflux.UI.Shared.Converters;

/// <summary>
/// 문자열이 비어 있으면 Collapsed, 값이 있으면 Visible로 변환합니다.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
