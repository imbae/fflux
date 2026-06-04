namespace fflux.Core.Abstractions;

/// <summary>
/// 미디어 파일의 특정 구간을 GIF 애니메이션으로 내보내는 인터페이스입니다.
/// ffmpeg.autogen AV_CODEC_ID_GIF + AV_PIX_FMT_PAL8 인코더를 사용합니다 (LGPL 안전).
/// </summary>
public interface IGifExporter
{
    /// <summary>
    /// 지정 구간을 GIF로 내보냅니다.
    /// </summary>
    /// <param name="sourcePath">원본 미디어 파일 경로</param>
    /// <param name="outputPath">출력 GIF 파일 경로</param>
    /// <param name="startTime">시작 위치</param>
    /// <param name="duration">내보낼 구간 길이</param>
    /// <param name="maxWidth">최대 가로 픽셀 (비율 유지, 0=원본 크기)</param>
    /// <param name="targetFps">GIF 프레임레이트 (0=소스 FPS 그대로)</param>
    /// <param name="progress">진행률 콜백 (0.0–1.0)</param>
    /// <param name="ct">취소 토큰</param>
    Task ExportAsync(
        string sourcePath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan duration,
        int maxWidth = 480,
        double targetFps = 10.0,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
