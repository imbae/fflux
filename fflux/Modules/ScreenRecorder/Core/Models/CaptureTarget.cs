namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

public enum CaptureMode { FullScreen, Region, Window }

public sealed record CaptureTarget(CaptureMode Mode, int MonitorIndex = 0);
