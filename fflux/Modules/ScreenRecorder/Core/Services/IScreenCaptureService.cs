using System.Windows;
using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

public interface IScreenCaptureService : IAsyncDisposable
{
    /// <summary>캡처 대상 모드 및 모니터 인덱스를 설정합니다.</summary>
    void SetCaptureTarget(CaptureTarget target);

    /// <summary>영역 캡처 범위를 설정합니다 (CaptureMode.Region 시 사용).</summary>
    void SetCaptureRegion(Rect region);

    /// <summary>윈도우 캡처 핸들을 설정합니다 (CaptureMode.Window 시 사용).</summary>
    void SetCaptureWindow(IntPtr hWnd);

    /// <summary>캡처 목표 FPS를 설정합니다 (기본값 30).</summary>
    void SetTargetFps(int fps);

    /// <summary>시스템에서 사용 가능한 모니터 목록을 반환합니다.</summary>
    IReadOnlyList<MonitorInfo> GetAvailableMonitors();

    /// <summary>
    /// DXGI Desktop Duplication으로 프레임을 비동기 스트림으로 캡처합니다.
    /// 취소될 때까지 계속 프레임을 yield합니다.
    /// </summary>
    IAsyncEnumerable<CaptureFrame> CaptureAsync(CancellationToken ct);
}
