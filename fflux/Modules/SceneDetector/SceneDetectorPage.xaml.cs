using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace fflux.UI.Modules.SceneDetector;

public partial class SceneDetectorPage : Page
{
    public SceneDetectorViewModel ViewModel { get; }

    private static readonly string[] MediaExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
         ".ts", ".m2ts", ".mp3", ".aac", ".wav", ".flac"];

    public SceneDetectorPage(SceneDetectorViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        DragOver += OnDragOver;
        Drop     += OnDrop;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var path = files[0];
        if (MediaExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            ViewModel.FilePath = path;
    }
}
