using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace fflux.UI.Modules.VideoAnalyzer;

public partial class VideoAnalyzerPage : Page
{
    public VideoAnalyzerViewModel ViewModel { get; }

    public VideoAnalyzerPage(VideoAnalyzerViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Drop       += OnDrop;
        DragOver   += OnDragOver;
    }

    private static readonly string[] MediaExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
         ".ts", ".m2ts", ".mp3", ".aac", ".wav", ".flac", ".ogg"];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var path = files[0];
        if (!MediaExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            return;

        await ViewModel.AnalyzeFileAsync(path);
    }
}
