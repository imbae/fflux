using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Views;

public partial class RegionPickerWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;

    private readonly MonitorInfo _monitor;
    private Point  _startDip;
    private bool   _dragging;
    private double _scaleX = 1.0;
    private double _scaleY = 1.0;

    private readonly TaskCompletionSource<Rect?> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Rect?> PickAsync() => _tcs.Task;

    public RegionPickerWindow(MonitorInfo monitor)
    {
        _monitor = monitor;

        // 생성 시점에 1차 DPI 추정 (기본 모니터 비율 사용) — Show() 전 위치 설정용
        double physW = GetSystemMetrics(SM_CXSCREEN);
        double dipW  = SystemParameters.PrimaryScreenWidth;
        if (physW > 0 && dipW > 0)
            _scaleX = _scaleY = physW / dipW;

        Left   = monitor.Left   / _scaleX;
        Top    = monitor.Top    / _scaleY;
        Width  = monitor.Width  / _scaleX;
        Height = monitor.Height / _scaleY;

        InitializeComponent();

        Loaded  += OnLoaded;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
    }

    // OnSourceInitialized: HwndSource 생성 직후 — Show() 이전 시점에 정확한 DPI 획득 가능
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is { } ct)
        {
            _scaleX = ct.TransformToDevice.M11;
            _scaleY = ct.TransformToDevice.M22;
            Left   = _monitor.Left   / _scaleX;
            Top    = _monitor.Top    / _scaleY;
            Width  = _monitor.Width  / _scaleX;
            Height = _monitor.Height / _scaleY;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 레이아웃 완료 후 힌트 텍스트 중앙 배치
        HintBorder.UpdateLayout();
        Canvas.SetLeft(HintBorder, (Width  - HintBorder.ActualWidth)  / 2);
        Canvas.SetTop (HintBorder, (Height - HintBorder.ActualHeight) / 2);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startDip               = e.GetPosition(RootCanvas);
        _dragging               = true;
        HintBorder.Visibility   = Visibility.Collapsed;
        SelectionRect.Visibility = Visibility.Visible;
        RootCanvas.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelectionRect(_startDip, e.GetPosition(RootCanvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        RootCanvas.ReleaseMouseCapture();

        var dipRect = MakeDipRect(_startDip, e.GetPosition(RootCanvas));

        if (dipRect.Width > 5 && dipRect.Height > 5)
        {
            // DIP → 물리 픽셀 (모니터 좌상단 기준 상대 좌표)
            _tcs.TrySetResult(new Rect(
                dipRect.X * _scaleX,
                dipRect.Y * _scaleY,
                dipRect.Width  * _scaleX,
                dipRect.Height * _scaleY));
        }
        else
        {
            _tcs.TrySetResult(null);
        }

        Close();
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        Close();
    }

    private void UpdateSelectionRect(Point a, Point b)
    {
        var r = MakeDipRect(a, b);
        Canvas.SetLeft(SelectionRect, r.X);
        Canvas.SetTop (SelectionRect, r.Y);
        SelectionRect.Width  = r.Width;
        SelectionRect.Height = r.Height;
    }

    private static Rect MakeDipRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}
