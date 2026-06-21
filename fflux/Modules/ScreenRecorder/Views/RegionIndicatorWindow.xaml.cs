using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace fflux.UI.Modules.ScreenRecorder.Views;

/// <summary>
/// 캡처 대상 영역을 시각적으로 표시하는 투명 오버레이.
/// Preview(녹화 전): 노란 테두리 + 검정 40% 배경, 드래그/리사이즈 가능.
/// Recording(녹화 중): 빨간 테두리 + 투명 배경, WS_EX_TRANSPARENT 적용(클릭 투과).
/// </summary>
public partial class RegionIndicatorWindow : Window
{
    // ── Win32 ──────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern int  GetSystemMetrics(int n);
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int n);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int n, int v);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private const int  GWL_EXSTYLE           = -20;
    private const int  WS_EX_TRANSPARENT     = 0x00000020;
    private const int  SM_CXSCREEN           = 0;
    // 이 창을 DXGI/BitBlt 화면 캡처에서 제외합니다 (Windows 10 2004+).
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private static readonly SolidColorBrush PreviewBg =
        new(Color.FromArgb(0x66, 0, 0, 0)); // 40% 불투명 검정

    // ── 상태 ───────────────────────────────────────────────────

    public Rect PhysRect { get; private set; }
    private double _scaleX = 1.0, _scaleY = 1.0;

    // ── 드래그 상태 ─────────────────────────────────────────────

    private enum Zone { None, Move, N, S, W, E, NW, NE, SW, SE }
    private Zone  _dragZone;
    private POINT _dragStart;     // GetCursorPos 기준 물리 픽셀
    private Rect  _dragStartRect; // 드래그 시작 시점의 PhysRect

    /// <summary>드래그/리사이즈로 영역이 확정될 때 발생 (물리 픽셀 스크린 절대좌표).</summary>
    public event EventHandler<Rect>? RegionChanged;

    // ── 생성자 ──────────────────────────────────────────────────

    public RegionIndicatorWindow(Rect physicalScreenRect)
    {
        PhysRect = physicalScreenRect;

        // Show() 이전 1차 DPI 추정
        double physW = GetSystemMetrics(SM_CXSCREEN);
        double dipW  = SystemParameters.PrimaryScreenWidth;
        if (physW > 0 && dipW > 0) _scaleX = _scaleY = physW / dipW;

        ApplyBounds();
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // 정밀 DPI 획득 후 재배치 (윈도우 핸들 생성 직후, Show() 직전)
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is { } ct)
        {
            _scaleX = ct.TransformToDevice.M11;
            _scaleY = ct.TransformToDevice.M22;
            ApplyBounds();
        }

        // 이 창을 DXGI Desktop Duplication / BitBlt 캡처에서 제외합니다.
        // 녹화 영상에 인디케이터 테두리가 찍히지 않습니다 (Windows 10 2004+).
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    // ── 공개 API ─────────────────────────────────────────────────

    public void UpdateBounds(Rect physicalScreenRect)
    {
        PhysRect = physicalScreenRect;
        ApplyBounds();
    }

    /// <summary>녹화 전 상태: 노란 테두리, 40% 검정 배경, 드래그 인터랙션 활성.</summary>
    public void SetPreviewMode()
    {
        Outline.BorderBrush        = Brushes.Yellow;
        Outline.Background         = PreviewBg;
        HandleLayer.Visibility     = Visibility.Visible;
        HitLayer.IsHitTestVisible  = true;
        SetClickThrough(false);
    }

    /// <summary>녹화 중 상태: 빨간 테두리, 투명 배경, 클릭 완전 투과.</summary>
    public void SetRecordingMode()
    {
        Outline.BorderBrush        = Brushes.Red;
        Outline.Background         = Brushes.Transparent;
        HandleLayer.Visibility     = Visibility.Collapsed;
        HitLayer.IsHitTestVisible  = false;
        SetClickThrough(true);
    }

    // ── 마우스 핸들러 ────────────────────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragZone != Zone.None && e.LeftButton == MouseButtonState.Pressed)
        {
            // 드래그 중: GetCursorPos 기반 물리 픽셀 델타로 영역 업데이트
            GetCursorPos(out var cur);
            double dx = cur.X - _dragStart.X;
            double dy = cur.Y - _dragStart.Y;
            PhysRect = ApplyDrag(_dragStartRect, dx, dy, _dragZone);
            ApplyBounds();
        }
        else
        {
            // 호버: 위치 기반 커서 갱신
            HitLayer.Cursor = CursorFor(ZoneAt(e.GetPosition(HitLayer)));
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _dragZone      = ZoneAt(e.GetPosition(HitLayer));
        _dragStartRect = PhysRect;
        GetCursorPos(out _dragStart);
        HitLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragZone == Zone.None) return;
        HitLayer.ReleaseMouseCapture();
        _dragZone = Zone.None;
        RegionChanged?.Invoke(this, PhysRect); // 확정 → ViewModel에 통보
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────

    // 마우스 위치(DIP)로 조작 구역 결정
    private Zone ZoneAt(Point p)
    {
        const double edge = 12.0; // 코너/엣지 감지 범위 (DIP)
        bool l = p.X < edge,      r = p.X > Width  - edge;
        bool t = p.Y < edge,      b = p.Y > Height - edge;
        if (l && t) return Zone.NW;
        if (r && t) return Zone.NE;
        if (l && b) return Zone.SW;
        if (r && b) return Zone.SE;
        if (t)      return Zone.N;
        if (b)      return Zone.S;
        if (l)      return Zone.W;
        if (r)      return Zone.E;
        return Zone.Move;
    }

    private static Cursor CursorFor(Zone z) => z switch
    {
        Zone.NW or Zone.SE => Cursors.SizeNWSE,
        Zone.NE or Zone.SW => Cursors.SizeNESW,
        Zone.N  or Zone.S  => Cursors.SizeNS,
        Zone.W  or Zone.E  => Cursors.SizeWE,
        _                  => Cursors.SizeAll
    };

    // 드래그 델타(물리 픽셀)를 적용해 새 Rect 계산
    private static Rect ApplyDrag(Rect r, double dx, double dy, Zone z)
    {
        const double min = 40.0; // 최소 크기 (물리 픽셀)
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;

        switch (z)
        {
            case Zone.Move: x += dx; y += dy;                  break;
            case Zone.N:             y += dy; h -= dy;          break;
            case Zone.S:                      h += dy;          break;
            case Zone.W:    x += dx;          w -= dx;          break;
            case Zone.E:                      w += dx;          break;
            case Zone.NW:   x += dx; y += dy; w -= dx; h -= dy; break;
            case Zone.NE:            y += dy; w += dx; h -= dy; break;
            case Zone.SW:   x += dx;          w -= dx; h += dy; break;
            case Zone.SE:                     w += dx; h += dy; break;
        }

        // 최소 크기 보장 (좌/상 엣지 드래그 시 반대 엣지 고정)
        if (w < min) { if (z is Zone.W or Zone.NW or Zone.SW) x = r.Right  - min; w = min; }
        if (h < min) { if (z is Zone.N or Zone.NW or Zone.NE) y = r.Bottom - min; h = min; }

        return new Rect(x, y, w, h);
    }

    private void ApplyBounds()
    {
        Left   = PhysRect.X      / _scaleX;
        Top    = PhysRect.Y      / _scaleY;
        Width  = PhysRect.Width  / _scaleX;
        Height = PhysRect.Height / _scaleY;
    }

    // WS_EX_TRANSPARENT 토글: true = 모든 마우스 입력 투과
    private void SetClickThrough(bool on)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // 아직 핸들 미생성
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
    }
}
