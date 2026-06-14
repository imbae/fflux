using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Views;

public partial class DrawingOverlayWindow : Window
{
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public DrawingOverlayViewModel ViewModel { get; }

    public DrawingOverlayWindow(DrawingOverlayViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // 전체 모니터 크기로 설정 (주 모니터)
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize(InkLayer, ShapeLayer, this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }

    // ── Win32 클릭 통과 ─────────────────────────────────────────

    public void SetClickThrough(bool clickThrough)
    {
        var hwnd  = new WindowInteropHelper(this).EnsureHandle();
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (clickThrough)
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
        else
            SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
    }

    // ── 마우스 이벤트 → ViewModel 위임 ─────────────────────────────

    private void OnShapeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedTool == DrawingTool.Pen) return;
        ShapeLayer.CaptureMouse();
        ViewModel.OnMouseDown(e.GetPosition(ShapeLayer));
        e.Handled = true;
    }

    private void OnShapeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        ViewModel.OnMouseMove(e.GetPosition(ShapeLayer));
    }

    private void OnShapeMouseUp(object sender, MouseButtonEventArgs e)
    {
        ShapeLayer.ReleaseMouseCapture();
        ViewModel.OnMouseUp(e.GetPosition(ShapeLayer));
        e.Handled = true;
    }

    // ── 툴바 드래그 (Toolbar 영역 드래그로 창 이동) ─────────────────

    private void OnToolbarDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
