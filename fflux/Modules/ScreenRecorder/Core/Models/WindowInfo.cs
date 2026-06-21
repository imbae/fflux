using System.Windows;

namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

public sealed record WindowInfo(IntPtr Handle, string Title, Rect Bounds)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? $"[창 0x{Handle:X}]" : Title;
}
