using System.Globalization;
using System.Windows.Data;

namespace fflux.UI.Shared.Converters;

/// <summary>
/// null 이면 Collapsed, null 이 아니면 Visible로 변환합니다.
/// Profile, Language 등 선택적 필드의 행 숨김에 사용합니다.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value is null;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? isNull : !isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
