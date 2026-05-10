using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>ffprobe를 이용한 비트레이트 분석 서비스 인터페이스.</summary>
public interface IBitrateAnalysisService
{
    /// <summary>
    /// 미디어 파일의 초당 비트레이트와 프레임 타입 분포를 분석합니다.
    /// </summary>
    /// <param name="ffprobeExePath">ffprobe.exe 전체 경로</param>
    /// <param name="inputFile">분석할 미디어 파일 경로</param>
    /// <param name="progress">단계 진행 상황 콜백 (stage 이름, 처리된 프레임 수)</param>
    /// <param name="ct">취소 토큰</param>
    Task<BitrateAnalysisResult> AnalyzeAsync(
        string                                         ffprobeExePath,
        string                                         inputFile,
        IProgress<(string Stage, int FramesProcessed)> progress,
        CancellationToken                              ct = default);
}
