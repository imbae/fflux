using System.Collections.Specialized;
using System.Windows.Controls;

namespace fflux.UI.Modules.AiSubtitle;

public partial class AiSubtitlePage : Page
{
    private AiSubtitleViewModel? _vm;

    public AiSubtitlePage(AiSubtitleViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        // 로그 컬렉션 변경 시 자동 스크롤
        _vm.Logs.CollectionChanged += OnLogsChanged;

        Unloaded += (_, _) =>
        {
            if (_vm != null)
                _vm.Logs.CollectionChanged -= OnLogsChanged;
        };
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.InvokeAsync(() => LogScrollViewer.ScrollToBottom());
    }
}
