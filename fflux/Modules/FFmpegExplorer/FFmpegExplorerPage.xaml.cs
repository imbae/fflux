using System.Windows.Controls;

namespace fflux.UI.Modules.FFmpegExplorer;

public partial class FFmpegExplorerPage : Page
{
    public FFmpegExplorerViewModel ViewModel { get; }

    public FFmpegExplorerPage(FFmpegExplorerViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => LogTextBox.TextChanged += (_, _) => LogTextBox.ScrollToEnd();
    }
}
