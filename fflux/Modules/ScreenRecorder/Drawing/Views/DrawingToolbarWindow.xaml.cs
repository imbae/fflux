using System.Windows;
using System.Windows.Input;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Views;

/// <summary>
/// 그리기 툴바 독립 창. DrawingOverlayWindow(클릭 통과)와 달리 이 창은 클릭을 수신합니다.
/// 사용자가 드래그로 화면 내 자유롭게 이동할 수 있습니다.
/// </summary>
public partial class DrawingToolbarWindow : Window
{
    public DrawingToolbarWindow(DrawingOverlayViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // SizeToContent로 크기 확정 후 화면 상단 중앙에 배치
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.PrimaryScreenWidth / 2 - ActualWidth  / 2;
        Top  = 12;
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
