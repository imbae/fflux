using System.Windows.Controls;

namespace fflux.UI.Modules.MisbViewer;

/// <summary>
/// VMTI 바운딩 박스 + 레이블 오버레이 컨트롤.
/// DataContext는 PlayerViewModel에서 상속됩니다.
/// </summary>
public partial class MisbOverlayControl : UserControl
{
    public MisbOverlayControl()
    {
        InitializeComponent();
    }
}
