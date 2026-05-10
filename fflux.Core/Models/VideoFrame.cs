using System.Buffers;

namespace fflux.Core.Models;

/// <summary>
/// 디코딩 및 픽셀 포맷 변환이 완료된 단일 비디오 프레임입니다.
/// 픽셀 데이터는 항상 <b>BGRA 32-bit</b> 형식으로 제공되며
/// WPF <c>WriteableBitmap</c>에 직접 복사할 수 있습니다.
/// </summary>
/// <remarks>
/// <see cref="Dispose"/>를 호출하면 <c>ArrayPool</c>에서 임대한 픽셀 버퍼를
/// 반환합니다. 렌더링이 끝난 후 반드시 해제하세요.
/// </remarks>
public sealed class VideoFrame : IDisposable
{
    private readonly bool _isPooled;
    private bool _disposed;

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
    /// 유효 데이터 크기 = <see cref="Height"/> × <see cref="Stride"/>.
    /// ArrayPool 임대 시 배열이 요청보다 클 수 있으므로 Length 대신 Height×Stride를 사용하세요.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>영상 내 이 프레임의 표시 시각(PTS) 기반 타임스탬프</summary>
    public TimeSpan Timestamp { get; }

    /// <param name="isPooled">true이면 Dispose() 시 Data를 ArrayPool에 반환합니다.</param>
    public VideoFrame(int width, int height, int stride, byte[] data, TimeSpan timestamp,
                      bool isPooled = false)
    {
        Width     = width;
        Height    = height;
        Stride    = stride;
        Data      = data;
        Timestamp = timestamp;
        _isPooled = isPooled;
    }

    /// <summary>
    /// 픽셀 버퍼를 <c>ArrayPool</c>에 반환합니다.
    /// 렌더링(WritePixels) 완료 후 호출하세요. 이중 해제는 안전하게 무시됩니다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_isPooled)
            ArrayPool<byte>.Shared.Return(Data);
    }
}
