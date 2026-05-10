using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>FFmpeg 네이티브 라이브러리의 초기화를 담당하는 서비스 인터페이스입니다.</summary>
public interface IFFmpegInitializer
{
    /// <summary>FFmpeg 바이너리가 성공적으로 로드된 상태인지를 나타냅니다.</summary>
    bool IsInitialized { get; }

    /// <summary>현재 로드된 FFmpeg 바이너리의 폴더 경로입니다.</summary>
    string LoadedBinaryPath { get; }

    /// <summary>로드된 FFmpeg 라이브러리의 버전 정보입니다. 초기화 전이면 null입니다.</summary>
    FFmpegVersionInfo? VersionInfo { get; }

    /// <summary>
    /// 지정된 경로에서 FFmpeg LGPL 바이너리를 로드하고 초기화합니다.
    /// </summary>
    /// <param name="binaryPath">avcodec, avformat 등 DLL이 있는 폴더 경로</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="Exceptions.FFmpegInitializationException">
    ///     바이너리를 찾을 수 없거나 로드에 실패한 경우
    /// </exception>
    Task InitializeAsync(string binaryPath, CancellationToken ct = default);
}
