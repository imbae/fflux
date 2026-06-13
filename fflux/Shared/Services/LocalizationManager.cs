using fflux.UI.Shared.Models;
using System.Globalization;
using System.Resources;

namespace fflux.UI.Shared.Services;

/// <summary>
/// 런타임 언어 전환을 지원하는 싱글톤 로컬라이제이션 관리자.
/// WPF 바인딩 인덱서 패턴: {loc:L Key} 마크업 확장으로 XAML에서 사용합니다.
/// </summary>
public sealed partial class LocalizationManager : ObservableObject
{
    private static readonly LocalizationManager _instance = new();
    public static LocalizationManager Instance => _instance;

    private static readonly ResourceManager _rm =
        new("fflux.UI.Resources.Strings", typeof(LocalizationManager).Assembly);

    private CultureInfo _culture = new("ko");

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Korean;

    /// <summary>WPF 바인딩 인덱서 — {Binding [Key], Source={x:Static loc:LocalizationManager.Instance}}</summary>
    public string this[string key]
    {
        get
        {
            try { return _rm.GetString(key, _culture) ?? $"[{key}]"; }
            catch { return $"[{key}]"; }
        }
    }

    /// <summary>언어를 변경하고 모든 바인딩을 갱신합니다.</summary>
    public void SetLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        _culture = language switch
        {
            AppLanguage.English => new CultureInfo("en"),
            _ => new CultureInfo("ko"),
        };
        _rm.ReleaseAllResources();
        Thread.CurrentThread.CurrentUICulture = _culture;
        Thread.CurrentThread.CurrentCulture = _culture;
        // "Item[]" — WPF 인덱서 바인딩 전체 갱신
        OnPropertyChanged("Item[]");
    }
}
