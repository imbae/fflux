using System.Globalization;
using System.Windows.Data;

namespace fflux.UI.Shared.Converters;

/// <summary>bool 값을 반전합니다 (true → false, false → true).</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
