using System.Windows.Controls;

namespace fflux.UI.Modules.AiSubtitle;

/// <summary>AI 자막 생성 페이지 코드-비하인드.</summary>
public partial class AiSubtitlePage : Page
{
    public AiSubtitlePage(AiSubtitleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
