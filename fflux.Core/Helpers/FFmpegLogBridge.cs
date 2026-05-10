namespace fflux.Core.Helpers;

/// <summary>
/// FFmpeg의 av_log 콜백을 .NET <see cref="ILogger"/>로 연결하는 브릿지입니다.
/// </summary>
/// <remarks>
/// 네이티브 콜백 포인터가 GC에 의해 수집되지 않도록
/// 델리게이트를 static 필드에 보관합니다.
/// </remarks>
internal static class FFmpegLogBridge
{
    // ── GC 수집 방지: 네이티브 FFmpeg이 이 포인터를 계속 참조하므로
    //    static 필드로 수명을 앱 전체에 고정합니다.
    private static av_log_set_callback_callback? _nativeCallback;

    // ILogger는 초기화 후 한 번만 할당되므로 volatile이면 충분합니다.
    private static volatile ILogger? _logger;

    /// <summary>av_log 콜백을 등록하고 <paramref name="logger"/>로 전달합니다.</summary>
    internal static unsafe void Initialize(ILogger logger)
    {
        _logger = logger;
        _nativeCallback = OnAvLog;
        ffmpeg.av_log_set_callback(_nativeCallback);
    }

    // ── av_log 콜백 구현 ─────────────────────────────────────────────
    private static unsafe void OnAvLog(void* avcl, int level, string fmt, byte* vl)
    {
        if (_logger is null) return;

        // AV_LOG_QUIET(-8) 이하는 출력을 완전히 억제합니다.
        if (level < ffmpeg.AV_LOG_PANIC) return;

        var logLevel = MapLogLevel(level);
        if (!_logger.IsEnabled(logLevel)) return;

        // FFmpeg 자체 포매터를 사용해 printf 형식 문자열을 확장합니다.
        const int bufSize = 1024;
        var buf = stackalloc byte[bufSize];
        int printPrefix = 1;
        ffmpeg.av_log_format_line(avcl, level, fmt, vl, buf, bufSize, &printPrefix);

        var message = Marshal.PtrToStringUTF8((IntPtr)buf)
            ?.TrimEnd('\n', '\r', ' ', '\0');

        if (!string.IsNullOrWhiteSpace(message))
            _logger.Log(logLevel, "[FFmpeg] {Message}", message);
    }

    // ── FFmpeg 로그 레벨 → .NET LogLevel 매핑 ───────────────────────
    private static LogLevel MapLogLevel(int avLevel)
    {
        if (avLevel <= ffmpeg.AV_LOG_FATAL)   return LogLevel.Critical;
        if (avLevel <= ffmpeg.AV_LOG_ERROR)   return LogLevel.Error;
        if (avLevel <= ffmpeg.AV_LOG_WARNING) return LogLevel.Warning;
        if (avLevel <= ffmpeg.AV_LOG_INFO)    return LogLevel.Information;
        if (avLevel <= ffmpeg.AV_LOG_VERBOSE) return LogLevel.Debug;
        return LogLevel.Trace; // AV_LOG_DEBUG, AV_LOG_TRACE
    }
}
