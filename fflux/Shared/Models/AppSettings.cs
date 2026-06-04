namespace fflux.UI.Shared.Models;

/// <summary>앱 설정 루트 모델 (appsettings.local.json으로 영속)</summary>
public sealed class AppSettings
{
    /// <summary>FFmpeg LGPL 바이너리 폴더 경로 (ffmpeg.exe가 있는 폴더)</summary>
    public string FFmpegBinaryPath { get; set; } = string.Empty;

    /// <summary>기본 출력 폴더 경로</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    /// <summary>앱 테마</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>앱 언어 (추후 지원 예정)</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Korean;

    /// <summary>모든 소스에 공통 적용되는 디코더 옵션</summary>
    public DecoderOptions Decoder { get; set; } = new();

    /// <summary>네트워크/라이브 스트리밍 전용 FFmpeg 옵션</summary>
    public StreamingOptions Streaming { get; set; } = new();
}

/// <summary>
/// 파일·스트리밍·캡처 장치 모든 소스에 공통 적용되는 디코더 옵션입니다.
/// </summary>
public sealed class DecoderOptions
{
    /// <summary>
    /// 하드웨어 가속 종류 (none / auto / d3d11va / dxva2 / cuda / qsv).
    /// 초기화 실패 시 자동으로 소프트웨어 디코딩으로 전환됩니다.
    /// 기본값: none
    /// </summary>
    public string HwAccel { get; set; } = "none";

    /// <summary>
    /// 파일 재생 디코더 스레드 수. 0 = CPU 코어 수 자동.
    /// 기본값: 0
    /// </summary>
    public int FileThreadCount { get; set; } = 0;

    /// <summary>
    /// H.264/HEVC 디블록킹 필터 건너뜀 수준 (none / nonref / all).
    /// CPU 부하를 줄이는 대신 경계 부근 화질이 저하될 수 있습니다.
    /// 기본값: none
    /// </summary>
    public string SkipLoopFilter { get; set; } = "none";

    /// <summary>
    /// 디코딩할 프레임 종류 제한 (none / nonref / nonkey / all).
    /// nonref = 참조 프레임만, nonkey = 키프레임만, all = 전부 건너뜀.
    /// 기본값: none
    /// </summary>
    public string SkipFrame { get; set; } = "none";
}

/// <summary>
/// 라이브/스트리밍 재생 시 FFmpeg에 전달하는 네트워크 옵션입니다.
/// </summary>
public sealed class StreamingOptions
{
    /// <summary>RTSP 전송 프로토콜 (tcp / udp / http). 기본값: tcp</summary>
    public string RtspTransport { get; set; } = "tcp";

    /// <summary>연결 타임아웃 (초). 기본값: 10</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>스트림 분석 최대 데이터 크기 (KB). 기본값: 500</summary>
    public int ProbeSizeKb { get; set; } = 500;

    /// <summary>스트림 정보 분석 최대 시간 (초). 기본값: 1.0</summary>
    public double AnalyzeDurationSeconds { get; set; } = 1.0;

    /// <summary>fflags=nobuffer — 내부 버퍼링 비활성화. 기본값: true</summary>
    public bool NoBuffer { get; set; } = true;

    /// <summary>A/V 디먹싱 최대 지연 (ms). 기본값: 500</summary>
    public int MaxDelayMs { get; set; } = 500;

    /// <summary>라이브 디코더 스레드 수 (1 권장). 기본값: 1</summary>
    public int LiveThreadCount { get; set; } = 1;

    /// <summary>소켓 수신 버퍼 크기 (KB). 0 = 시스템 기본값. 기본값: 0</summary>
    public int RecvBufferSizeKb { get; set; } = 0;

    /// <summary>RTP 패킷 재정렬 큐 크기 (패킷 수). 0 = 비활성화. 기본값: 500</summary>
    public int ReorderQueueSize { get; set; } = 500;

    /// <summary>연결 끊김 시 자동 재연결 여부. 기본값: false</summary>
    public bool Reconnect { get; set; } = false;

    /// <summary>자동 재연결 최대 대기 시간 (초). 0 = 즉시. 기본값: 0</summary>
    public int ReconnectDelayMaxSeconds { get; set; } = 0;
}

/// <summary>앱 테마 선택 열거형</summary>
public enum AppTheme
{
    /// <summary>시스템 설정 따름</summary>
    System,
    /// <summary>라이트 테마</summary>
    Light,
    /// <summary>다크 테마</summary>
    Dark
}

/// <summary>앱 언어 선택 열거형 (추후 지원)</summary>
public enum AppLanguage
{
    Korean,
    English
}
