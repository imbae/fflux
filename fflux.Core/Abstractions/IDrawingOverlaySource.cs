namespace fflux.Core.Abstractions;

/// <summary>
/// 그리기 오버레이 레이어의 현재 픽셀 데이터를 제공합니다.
/// RecordingSessionService가 캡처 프레임과 합성하는 데 사용합니다.
/// </summary>
public interface IDrawingOverlaySource
{
    /// <summary>현재 그리기 레이어에 내용이 있고 표시 중이면 true.</summary>
    bool HasVisibleContent { get; }

    /// <summary>
    /// UI 스레드에서 그리기 레이어를 <paramref name="width"/>×<paramref name="height"/> 크기로 래스터화하여
    /// PBGRA32 픽셀 배열을 반환합니다. 내용 없음 또는 비활성이면 null.
    /// </summary>
    Task<byte[]?> GetPixelsAsync(int width, int height);
}
