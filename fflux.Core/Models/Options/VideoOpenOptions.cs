namespace fflux.Core.Models.Options;

/// <summary>
/// <see cref="Abstractions.IVideoDecoder.OpenAsync"/> 에 전달되는 재생 옵션입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>공통 디코더 옵션은 파일·스트리밍·캡처 장치 모든 소스에 적용됩니다.</item>
///   <item>스트리밍 전용 옵션은 네트워크 URL (rtsp://, rtp://, udp://, http:// 등)에만 적용됩니다.</item>
/// </list>
/// </remarks>
public sealed class VideoOpenOptions
{
    // ══════════════════════════════════════════════════════════════════
    // 공통 디코더 옵션 — 모든 소스에 적용
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 하드웨어 가속 종류.
    /// 유효값: none / auto / d3d11va / dxva2 / cuda / qsv.
    /// 초기화 실패 시 자동으로 소프트웨어 디코딩으로 전환됩니다.
    /// 기본값: "none"
    /// </summary>
    public string HwAccel { get; init; } = "none";

    /// <summary>
    /// 파일 재생 디코더 스레드 수.
    /// 0 = CPU 코어 수 자동.
    /// 기본값: 0
    /// </summary>
    public int FileThreadCount { get; init; } = 0;

    /// <summary>
    /// H.264 / HEVC 디블록킹 필터 건너뜀 수준.
    /// 유효값: none / nonref / all.
    /// CPU 부하를 줄이는 대신 경계 부근 화질이 저하될 수 있습니다.
    /// 기본값: "none"
    /// </summary>
    public string SkipLoopFilter { get; init; } = "none";

    /// <summary>
    /// 디코딩할 프레임 종류 제한.
    /// 유효값: none / nonref / nonkey / all.
    /// nonref = 참조 프레임만, nonkey = 키프레임만, all = 전부 건너뜀.
    /// 기본값: "none"
    /// </summary>
    public string SkipFrame { get; init; } = "none";

    // ══════════════════════════════════════════════════════════════════
    // 스트리밍 전용 옵션 — 네트워크 URL에만 적용
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// RTSP 전송 프로토콜 (tcp / udp / http).
    /// rtsp:// URL에만 적용됩니다.
    /// 기본값: "tcp"
    /// </summary>
    public string RtspTransport { get; init; } = "tcp";

    /// <summary>
    /// 연결 타임아웃 (초). FFmpeg stimeout / timeout 옵션으로 변환됩니다.
    /// 기본값: 10
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// 스트림 분석 최대 데이터 크기 (KB). probesize 옵션으로 변환됩니다.
    /// 기본값: 500 KB
    /// </summary>
    public int ProbeSizeKb { get; init; } = 500;

    /// <summary>
    /// 스트림 정보 분석 최대 시간 (초). analyzeduration 옵션으로 변환됩니다.
    /// 기본값: 1.0 초
    /// </summary>
    public double AnalyzeDurationSeconds { get; init; } = 1.0;

    /// <summary>
    /// FFmpeg 내부 패킷 버퍼링 비활성화 (fflags=nobuffer).
    /// 기본값: true
    /// </summary>
    public bool NoBuffer { get; init; } = true;

    /// <summary>
    /// A/V 디먹싱 최대 지연 (ms). max_delay 옵션으로 변환됩니다.
    /// 기본값: 500 ms
    /// </summary>
    public int MaxDelayMs { get; init; } = 500;

    /// <summary>
    /// 라이브 스트리밍 디코더 스레드 수.
    /// FF_THREAD_FRAME 사용 시 (N-1)프레임 추가 지연이 발생하므로 1 권장.
    /// 기본값: 1
    /// </summary>
    public int LiveThreadCount { get; init; } = 1;

    /// <summary>
    /// 소켓 수신 버퍼 크기 (KB).
    /// 0 = 시스템 기본값. UDP 스트림의 패킷 손실 방지에 효과적입니다.
    /// 기본값: 0
    /// </summary>
    public int RecvBufferSizeKb { get; init; } = 0;

    /// <summary>
    /// RTP 패킷 재정렬 큐 크기 (패킷 수).
    /// 0 = 비활성화. UDP 스트림에서 순서가 뒤바뀐 패킷을 재정렬합니다.
    /// 기본값: 500
    /// </summary>
    public int ReorderQueueSize { get; init; } = 500;

    /// <summary>
    /// 연결 끊김 시 자동 재연결 여부.
    /// 기본값: false
    /// </summary>
    public bool Reconnect { get; init; } = false;

    /// <summary>
    /// 자동 재연결 최대 대기 시간 (초). 0 = 즉시 재연결.
    /// 기본값: 0
    /// </summary>
    public int ReconnectDelayMaxSeconds { get; init; } = 0;

    /// <summary>기본값 인스턴스</summary>
    public static VideoOpenOptions Default { get; } = new();
}
