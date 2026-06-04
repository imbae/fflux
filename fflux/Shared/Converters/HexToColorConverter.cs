using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace fflux.UI.Shared.Converters;

/// <summary>
/// "#RRGGBB" 또는 빈 문자열 → <see cref="Color"/> 변환기.
/// 빈 문자열 또는 파싱 실패 시 White를 반환합니다.
/// </summary>
[ValueConversion(typeof(string), typeof(Color))]
public sealed class HexToColorConverter : IMultiValueConverter
{
    public static readonly HexToColorConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = values.FirstOrDefault() as string;
        if (string.IsNullOrWhiteSpace(hex)) return Colors.White;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch { return Colors.White; }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
