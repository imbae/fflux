using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Helpers;
using fflux.Core.Models;

namespace fflux.Core;

/// <summary>
/// FFmpeg 네이티브 라이브러리의 초기화를 담당하는 서비스입니다.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><see cref="FFmpegLoader"/>로 DLL 로드 및 버전 검증</item>
///   <item><see cref="FFmpegLogBridge"/>로 av_log → ILogger 연결</item>
///   <item>동일 경로로 재초기화 시도 시 no-op 처리</item>
/// </list>
/// </remarks>
public sealed class FFmpegInitializer : IFFmpegInitializer
{
    private readonly ILogger<FFmpegInitializer> _logger;

    // 중복 초기화를 방지하는 비동기 잠금
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // ── IFFmpegInitializer 구현 ──────────────────────────────────────

    /// <inheritdoc/>
    public bool IsInitialized { get; private set; }

    /// <inheritdoc/>
    public string LoadedBinaryPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public FFmpegVersionInfo? VersionInfo { get; private set; }

    // ── 생성자 ───────────────────────────────────────────────────────

    public FFmpegInitializer(ILogger<FFmpegInitializer> logger)
    {
        _logger = logger;
    }

    // ── 공개 메서드 ──────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// 이미 동일한 경로로 초기화된 경우 아무 작업도 수행하지 않습니다.
    /// 경로가 변경된 경우 재초기화합니다.
    /// </remarks>
    public async Task InitializeAsync(string binaryPath, CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 동일 경로로 이미 초기화된 경우 재사용
            if (IsInitialized &&
                string.Equals(LoadedBinaryPath, binaryPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("FFmpeg이 이미 초기화되어 있습니다. (경로: {Path})", binaryPath);
                return;
            }

            _logger.LogInformation("FFmpeg 바이너리 로딩 시작... 경로: {Path}", binaryPath);

            // DLL 로딩은 I/O 작업이므로 ThreadPool에서 실행합니다.
            var versionInfo = await Task
                .Run(() => FFmpegLoader.Load(binaryPath), ct)
                .ConfigureAwait(false);

            // av_log 콜백을 ILogger로 연결합니다.
            FFmpegLogBridge.Initialize(_logger);

            // 네트워크 프로토콜(RTSP, RTP, UDP, HTTP 등)을 활성화합니다.
            // 이 호출 없이는 rtsp:// · udp:// 등 URL을 avformat_open_input에 전달해도
            // "Protocol not found" 오류가 발생합니다.
            ffmpeg.avformat_network_init();

            // 상태 기록
            VersionInfo       = versionInfo;
            LoadedBinaryPath  = binaryPath;
            IsInitialized     = true;

            _logger.LogInformation(
                "FFmpeg 초기화 완료 — {VersionSummary}",
                versionInfo.ToString());
        }
        catch (FFmpegInitializationException ex)
        {
            // 초기화 실패 상태를 명확히 유지합니다.
            IsInitialized = false;
            _logger.LogError(ex, "FFmpeg 초기화 실패");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FFmpeg 초기화가 취소되었습니다.");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
