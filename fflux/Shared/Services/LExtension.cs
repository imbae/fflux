using System.Windows.Data;
using System.Windows.Markup;

namespace fflux.UI.Shared.Services;

/// <summary>
/// XAML 마크업 확장 — 로컬라이즈된 문자열을 WPF 바인딩으로 제공합니다.
///
/// 사용법:
///   xmlns:loc="clr-namespace:fflux.UI.Shared.Services"
///   Content="{loc:L Player.Open.Label}"
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }
    public LExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
