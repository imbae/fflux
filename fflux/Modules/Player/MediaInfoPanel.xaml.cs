using System.Windows.Controls;

namespace fflux.UI.Modules.Player;

/// <summary>
/// 미디어 파일의 컨테이너 및 스트림 정보를 표시하는 오버레이 패널입니다.
/// DataContext = <see cref="fflux.Core.Models.StreamInfo.MediaInfo"/>
/// </summary>
public partial class MediaInfoPanel : UserControl
{
    public MediaInfoPanel()
    {
        InitializeComponent();
    }
}
