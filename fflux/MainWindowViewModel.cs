using System.ComponentModel;
using fflux.UI.Shared.Services;

namespace fflux.UI;

public partial class MainWindowViewModel : ObservableObject
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    // ── 라이선스 ────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isPremiumUser = App.IsMisbEnabled || App.IsAiSubtitleEnabled;

    // ── StatusBar ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _currentFileName = "";

    [ObservableProperty]
    private string _playbackStatusText = "";

    [ObservableProperty]
    private string _playbackStatusIcon = "Stop24";

    // ── 내부 상태 (언어 변경 시 재번역용) ─────────────────────────────

    private bool   _isFileOpen;
    private string _playbackStatusKey = "MainWindow.Status.Stopped";

    public MainWindowViewModel()
    {
        _currentFileName    = Loc["Player.Status.Default"];
        _playbackStatusText = Loc[_playbackStatusKey];

        Loc.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isFileOpen)
            CurrentFileName = Loc["Player.Status.Default"];
        PlaybackStatusText = Loc[_playbackStatusKey];
    }

    // ── PlayerViewModel에서 호출하는 상태 설정 메서드 ────────────────

    /// <summary>파일/스트림을 열었을 때 파일명을 설정합니다.</summary>
    public void SetCurrentFile(string fileName)
    {
        _isFileOpen     = true;
        CurrentFileName = fileName;
    }

    /// <summary>파일을 닫았을 때 기본 안내 문자로 되돌립니다.</summary>
    public void ClearCurrentFile()
    {
        _isFileOpen     = false;
        CurrentFileName = Loc["Player.Status.Default"];
    }

    /// <summary>재생 상태 텍스트를 지역화 키로 설정합니다.</summary>
    public void SetPlaybackStatus(string locKey, string iconSymbol)
    {
        _playbackStatusKey  = locKey;
        PlaybackStatusText  = Loc[locKey];
        PlaybackStatusIcon  = iconSymbol;
    }
}
