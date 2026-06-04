using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>FFmpeg select 필터를 이용한 장면 전환 감지 서비스 인터페이스.</summary>
public interface ISceneDetectionService
{
    /// <summary>
    /// 지정한 임계값 이상의 장면 전환을 비동기로 감지합니다.
    /// </summary>
    /// <param name="ffmpegExePath">ffmpeg.exe 전체 경로</param>
    /// <param name="inputFile">분석할 미디어 파일 경로</param>
    /// <param name="threshold">장면 변화 감지 임계값 (0.0 ~ 1.0, 권장: 0.3 ~ 0.5)</param>
    /// <param name="progress">진행 상황 콜백</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>감지된 장면 목록과 전체 재생 길이를 담은 결과</returns>
    Task<SceneDetectionResult> DetectScenesAsync(
        string                            ffmpegExePath,
        string                            inputFile,
        double                            threshold,
        IProgress<SceneDetectionProgress> progress,
        CancellationToken                 ct = default);
}
