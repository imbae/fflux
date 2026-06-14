using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

/// <summary>
/// Win32 RegisterHotKey + WPF HwndSource 메시지 훅으로 전역 핫키를 처리합니다.
/// Initialize()는 메인 윈도우 로드 이후 한 번 호출해야 합니다.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    // MOD_NOREPEAT: 키를 누르고 있어도 반복 발동 방지
    private const uint MOD_NOREPEAT = 0x4000u;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly Dictionary<int, Action> _callbacks = new();

    private HwndSource? _source;
    private IntPtr      _hwnd;
    private bool        _initialized;
    private bool        _disposed;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
        => _logger = logger;

    public void Initialize()
    {
        if (_initialized || _disposed) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = Application.Current.MainWindow;
            if (win is null) return;

            var helper = new WindowInteropHelper(win);
            helper.EnsureHandle();
            _hwnd = helper.Handle;

            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);
            _initialized = true;
        });
    }

    public bool Register(int id, uint modifiers, uint virtualKey, Action callback)
    {
        if (_disposed) return false;
        if (!_initialized) Initialize();
        if (_hwnd == IntPtr.Zero) return false;

        bool ok = RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, virtualKey);
        if (ok)
            _callbacks[id] = callback;
        else
            _logger.LogWarning("핫키 등록 실패: id={Id}, vk=0x{VK:X2}", id, virtualKey);

        return ok;
    }

    public void Unregister(int id)
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, id);
        _callbacks.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys.ToList())
            Unregister(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _callbacks.TryGetValue(wParam.ToInt32(), out var cb))
        {
            cb();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
