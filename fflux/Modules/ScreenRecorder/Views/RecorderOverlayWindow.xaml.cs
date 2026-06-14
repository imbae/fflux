using System.Windows;
using fflux.UI.Modules.ScreenRecorder.Core.Services;

namespace fflux.UI.Modules.ScreenRecorder.Views;

public partial class RecorderOverlayWindow : Window
{
    public RecorderOverlayViewModel ViewModel { get; }

    // ScreenRecorderViewModel은 그리기 토글 커맨드에 쓰임 (XAML DataContext는 RecorderOverlayViewModel)
    public ScreenRecorderViewModel RecorderVm { get; }

    public RecorderOverlayWindow(IRecordingSessionService session, ScreenRecorderViewModel recorderVm)
    {
        RecorderVm  = recorderVm;
        ViewModel   = new RecorderOverlayViewModel(session, this);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
