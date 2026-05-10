using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>FFmpeg 커맨드 빌더 및 실행 서비스 인터페이스.</summary>
public interface IFFmpegCommandService
{
    /// <summary>
    /// 옵션으로부터 ffmpeg 명령줄 인수 문자열을 생성합니다.
    /// (실행 파일 경로 제외: <c>-y -i "in" ... "out"</c> 형식)
    /// </summary>
    string BuildArguments(FFmpegCommandOptions options);

    /// <summary>
    /// ffmpeg 프로세스를 비동기로 실행합니다.
    /// </summary>
    /// <param name="ffmpegExePath">ffmpeg.exe 전체 경로</param>
    /// <param name="options">인코딩 옵션</param>
    /// <param name="progress">진행률 콜백 (UI 스레드로 마샬링됨)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>ffmpeg 프로세스 종료 코드</returns>
    Task<int> ExecuteAsync(
        string                    ffmpegExePath,
        FFmpegCommandOptions      options,
        IProgress<FFmpegProgress> progress,
        CancellationToken         ct = default);
}
