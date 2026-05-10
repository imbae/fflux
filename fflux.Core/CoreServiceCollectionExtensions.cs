using fflux.Core.Abstractions;
using fflux.Core.Decoders;
using fflux.Core.Demuxers;
using fflux.Core.Parsers;
using fflux.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace fflux.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // ── FFmpeg 초기화 서비스 ─────────────────────────────────────
        // Singleton: 프로세스 전체에서 FFmpeg 바이너리는 한 번만 로드합니다.
        services.AddSingleton<IFFmpegInitializer, FFmpegInitializer>();

        // ── 미디어 파일 리더 ─────────────────────────────────────────
        // Transient: 각 호출마다 독립적인 AVFormatContext 수명을 가집니다.
        services.AddTransient<IMediaFileReader, MediaFileReader>();

        // ── 비디오 디코더 ────────────────────────────────────────────
        // Transient: 각 재생 세션마다 독립적인 AVFormatContext + AVCodecContext를 가집니다.
        services.AddTransient<IVideoDecoder, VideoDecoder>();

        // ── 오디오 디코더 ────────────────────────────────────────────
        // Transient: 각 재생 세션마다 독립적인 AVFormatContext + AVCodecContext + SwrContext를 가집니다.
        services.AddTransient<IAudioDecoder, AudioDecoder>();

        // ── 자막 파서 ────────────────────────────────────────────────
        // Singleton: 상태 없음. 확장자 키로 구분합니다.
        services.AddKeyedSingleton<ISubtitleParser, SrtParser>("srt");
        services.AddKeyedSingleton<ISubtitleParser, VttParser>("vtt");

        // ── FFmpeg 커맨드 서비스 ──────────────────────────────────────
        // Singleton: 상태 없음.
        services.AddSingleton<IFFmpegCommandService, FFmpegCommandService>();

        // ── 장면 감지 서비스 ──────────────────────────────────────────
        // Singleton: 상태 없음. 실행마다 새 Process를 생성합니다.
        services.AddSingleton<ISceneDetectionService, SceneDetectionService>();

        // ── 비트레이트 분석 서비스 ────────────────────────────────────
        // Singleton: 상태 없음. ffprobe 프로세스를 생성합니다.
        services.AddSingleton<IBitrateAnalysisService, BitrateAnalysisService>();

        return services;
    }
}
