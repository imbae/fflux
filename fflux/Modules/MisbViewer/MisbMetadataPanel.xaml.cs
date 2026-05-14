using System.Windows.Controls;

namespace fflux.UI.Modules.MisbViewer;

/// <summary>
/// MISB 메타데이터 패널 코드-비하인드.
/// DataContext = MisbMetadataDisplayModel (PlayerViewModel에서 주입).
/// </summary>
public partial class MisbMetadataPanel : UserControl
{
    public MisbMetadataPanel()
    {
        InitializeComponent();
    }
}
