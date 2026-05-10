using CommunityToolkit.Mvvm.ComponentModel;

namespace fflux.UI;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>현재 열려 있는 파일 이름 (하단 StatusBar 표시용)</summary>
    [ObservableProperty]
    private string _currentFileName = "파일을 열어주세요";

    /// <summary>재생 상태 텍스트 (하단 StatusBar 표시용)</summary>
    [ObservableProperty]
    private string _playbackStatusText = "정지";

    /// <summary>재생 상태 아이콘 심볼 이름</summary>
    [ObservableProperty]
    private string _playbackStatusIcon = "Stop24";

    // Phase 3에서 IMediaPlayer 이벤트 구독 후 위 프로퍼티 업데이트 예정
}
