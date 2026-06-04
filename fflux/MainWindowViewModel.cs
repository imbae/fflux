namespace fflux.UI;

public partial class MainWindowViewModel : ObservableObject
{
    // ── 라이선스 ────────────────────────────────────────────────────
    // modules/ 폴더에 Private DLL이 존재할 때 런타임으로 true 설정됩니다.
    // 컴파일 타임(#if MISB) 이 아닌 App.IsMisbEnabled / App.IsAiSubtitleEnabled 기준입니다.

    [ObservableProperty]
    private bool _isPremiumUser = App.IsMisbEnabled || App.IsAiSubtitleEnabled;

    // ── StatusBar ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _currentFileName = "파일을 열어주세요";

    [ObservableProperty]
    private string _playbackStatusText = "정지";

    [ObservableProperty]
    private string _playbackStatusIcon = "Stop24";
}
