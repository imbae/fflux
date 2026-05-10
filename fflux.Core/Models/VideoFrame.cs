namespace fflux.Core.Models;

/// <summary>
/// 디코딩 및 픽셀 포맷 변환이 완료된 단일 비디오 프레임입니다.
/// 픽셀 데이터는 항상 <b>BGRA 32-bit</b> 형식으로 제공되며
/// WPF <c>WriteableBitmap</c>에 직접 복사할 수 있습니다.
/// </summary>
public sealed class VideoFrame
{
    /// <summary>픽셀 너비 (px)</summary>
    public int Width { get; }

    /// <summary>픽셀 높이 (px)</summary>
    public int Height { get; }

    /// <summary>
    /// 한 행(row)의 바이트 수 (stride).
    /// BGRA 포맷이므로 <c>Width × 4</c>와 같습니다.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// BGRA 픽셀 데이터 (행 우선, 위에서 아래로).
    /// 크기 = <see cref="Height"/> × <see cref="Stride"/>.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>영상 내 이 프레임의 표시 시각(PTS) 기반 타임스탬프</summary>
    public TimeSpan Timestamp { get; }

    public VideoFrame(int width, int height, int stride, byte[] data, TimeSpan timestamp)
    {
        Width     = width;
        Height    = height;
        Stride    = stride;
        Data      = data;
        Timestamp = timestamp;
    }
}
