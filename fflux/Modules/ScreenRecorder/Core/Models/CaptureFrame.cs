namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

/// <summary>DXGI에서 캡처한 단일 프레임 데이터. 픽셀 포맷은 BGRA32.</summary>
public sealed record CaptureFrame(
    byte[] Data,
    int Width,
    int Height,
    long TimestampUs);
