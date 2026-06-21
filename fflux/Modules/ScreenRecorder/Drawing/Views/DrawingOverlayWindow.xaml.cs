using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Views;

/// <summary>
/// 전체화면 렌더링 전용 창. 항상 WS_EX_TRANSPARENT 상태로 좌클릭을 통과시킵니다.
/// 그리기 입력은 전역 마우스 훅(중간 버튼)으로 처리합니다.
/// </summary>
public partial class DrawingOverlayWindow : Window
{
    // ── Win32 상수 ───────────────────────────────────────────────
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WH_MOUSE_LL       = 14;
    private const int WM_MOUSEMOVE      = 0x0200;
    private const int WM_MBUTTONDOWN    = 0x0207;
    private const int WM_MBUTTONUP      = 0x0208;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT  pt;
        public uint   mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern int    GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int    SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc cb, IntPtr mod, uint tid);
    [DllImport("user32.dll")] private static extern bool   UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);

    // ── 필드 ─────────────────────────────────────────────────────
    public DrawingOverlayViewModel ViewModel { get; }

    private IntPtr             _mouseHook;
    private LowLevelMouseProc? _hookProc;   // GC 방지용 강한 참조
    private bool               _midDown;
    private bool               _didDrag;
    private int                _midDownX, _midDownY;

    // 드래그로 인정할 최소 이동 거리 (픽셀)
    private const int DragThreshold = 5;

    // ── 생성자 ───────────────────────────────────────────────────

    public DrawingOverlayWindow(DrawingOverlayViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 항상 클릭 통과 — 좌클릭은 하위 창에 전달됨
        MakeClickThrough();

        ViewModel.Initialize(DrawingCanvas, this);

        // 전역 마우스 훅 설치 (중간 버튼 드래그 그리기)
        _hookProc  = HookCallback;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        ViewModel.Cleanup();
    }

    private void MakeClickThrough()
    {
        var hwnd  = new WindowInteropHelper(this).EnsureHandle();
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
    }

    // ── 전역 마우스 훅 콜백 ──────────────────────────────────────
    // WH_MOUSE_LL 훅은 SetWindowsHookEx를 호출한 스레드(UI 스레드)에서 실행됩니다.

    // WH_MOUSE_LL 훅은 SetWindowsHookEx를 호출한 스레드(= UI 스레드)에서 실행된다.
    // Dispatcher.BeginInvoke로 비동기 큐잉하면 WM_MBUTTONUP 이후에도 이미 큐에 쌓인
    // OnMouseMove가 실행되어 상태가 어긋나므로, ViewModel 메서드를 직접 동기 호출한다.
    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var ms  = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            if (msg == WM_MBUTTONDOWN)
            {
                _midDown  = true;
                _didDrag  = false;
                _midDownX = ms.pt.x;
                _midDownY = ms.pt.y;
            }
            else if (msg == WM_MOUSEMOVE && _midDown)
            {
                if (!_didDrag)
                {
                    int dx = ms.pt.x - _midDownX;
                    int dy = ms.pt.y - _midDownY;
                    if (dx * dx + dy * dy > DragThreshold * DragThreshold)
                    {
                        _didDrag = true;
                        ViewModel.OnMouseDown(ToCanvasPoint(_midDownX, _midDownY));
                    }
                }
                if (_didDrag)
                    ViewModel.OnMouseMove(ToCanvasPoint(ms.pt.x, ms.pt.y));
            }
            else if (msg == WM_MBUTTONUP && _midDown)
            {
                _midDown = false;
                if (_didDrag)
                    ViewModel.OnMouseUp(ToCanvasPoint(ms.pt.x, ms.pt.y));
                // 드래그 없는 단순 클릭 → 아무것도 하지 않음
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    // 물리 픽셀(스크린 좌표) → Canvas 좌표(DIP) 변환
    private System.Windows.Point ToCanvasPoint(int screenX, int screenY)
        => DrawingCanvas.PointFromScreen(new System.Windows.Point(screenX, screenY));
}
