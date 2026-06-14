namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

public sealed record MonitorInfo(
    int Index,
    string DeviceName,
    int Width,
    int Height,
    int Left,
    int Top,
    bool IsPrimary)
{
    public string DisplayName =>
        $"{(IsPrimary ? "★ " : "")}모니터 {Index + 1}  {Width}×{Height}  ({DeviceName})";
}
