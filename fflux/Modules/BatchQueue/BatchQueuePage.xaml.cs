using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace fflux.UI.Modules.BatchQueue;

public partial class BatchQueuePage : Page
{
    public BatchQueueViewModel ViewModel { get; }

    private static readonly string[] MediaExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
         ".ts", ".m2ts", ".mp3", ".aac", ".wav", ".flac", ".ogg"];

    public BatchQueuePage(BatchQueueViewModel viewModel)
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
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var path in files)
        {
            if (MediaExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                ViewModel.AddJobFromPath(path);
        }
    }
}
