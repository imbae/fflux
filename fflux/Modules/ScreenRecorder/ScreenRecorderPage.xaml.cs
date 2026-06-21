using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace fflux.UI.Modules.ScreenRecorder;

public partial class ScreenRecorderPage : Page
{
    public ScreenRecorderViewModel ViewModel { get; }

    public ScreenRecorderPage(ScreenRecorderViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // 다른 메뉴로 이동 시 인디케이터 닫기 / 돌아올 때 재표시
        Loaded   += (_, _) => viewModel.OnPageNavigatedTo();
        Unloaded += (_, _) => viewModel.OnPageNavigatedAway();
    }
}
