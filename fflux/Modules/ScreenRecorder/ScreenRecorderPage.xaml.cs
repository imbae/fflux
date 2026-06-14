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
    }
}
