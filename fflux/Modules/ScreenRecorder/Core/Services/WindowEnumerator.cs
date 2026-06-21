using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

/// <summary>Win32 EnumWindows로 화면에 표시된 창 목록을 조회하는 내부 헬퍼.</summary>
internal static class WindowEnumerator
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static IReadOnlyList<WindowInfo> GetVisibleWindows()
    {
        var result      = new List<WindowInfo>();
        var shellWindow = GetShellWindow();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == shellWindow || !IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            if (GetWindowText(hWnd, sb, sb.Capacity) == 0) return true;

            string title = sb.ToString().Trim();
            if (string.IsNullOrEmpty(title)) return true;

            if (GetWindowRect(hWnd, out var r))
            {
                int w = r.Right  - r.Left;
                int h = r.Bottom - r.Top;
                if (w > 0 && h > 0)
                    result.Add(new WindowInfo(hWnd, title, new Rect(r.Left, r.Top, w, h)));
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }
}
