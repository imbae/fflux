namespace fflux.Core.Models.StreamInfo;

/// <summary>오디오 스트림의 메타데이터 정보입니다.</summary>
public sealed record AudioStreamInfo
{
    /// <summary>컨테이너 내 스트림 인덱스</summary>
    public int StreamIndex { get; init; }

    /// <summary>코덱 단축 이름 (예: "aac", "mp3", "flac")</summary>
    public string CodecName { get; init; } = string.Empty;

    /// <summary>코덱 전체 이름</summary>
    public string CodecLongName { get; init; } = string.Empty;

    /// <summary>샘플레이트 (Hz, 예: 48000, 44100)</summary>
    public int SampleRate { get; init; }

    /// <summary>채널 수 (1=모노, 2=스테레오, 6=5.1 ...)</summary>
    public int Channels { get; init; }

    /// <summary>채널 레이아웃 설명 (예: "stereo", "5.1(side)")</summary>
    public string ChannelLayout { get; init; } = string.Empty;

    /// <summary>스트림 비트레이트 (bps). 정보 없으면 0.</summary>
    public long BitRate { get; init; }

    /// <summary>샘플 포맷 (예: "fltp", "s16", "s32")</summary>
    public string SampleFormat { get; init; } = string.Empty;

    /// <summary>스트림 재생 길이. 알 수 없으면 <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>스트림 언어 태그 (예: "kor", "eng"). 없으면 null.</summary>
    public string? Language { get; init; }

    /// <summary>샘플레이트를 kHz 단위 문자열로 반환합니다.</summary>
    public string SampleRateText => $"{SampleRate / 1000.0:F1} kHz";

    /// <summary>비트레이트를 kbps 단위 문자열로 반환합니다. 정보 없으면 null.</summary>
    public string? BitRateText => BitRate > 0 ? $"{BitRate / 1000} kbps" : null;
}
