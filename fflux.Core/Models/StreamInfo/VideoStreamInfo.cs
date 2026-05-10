namespace fflux.Core.Models.StreamInfo;

/// <summary>비디오 스트림의 메타데이터 정보입니다.</summary>
public sealed record VideoStreamInfo
{
    /// <summary>컨테이너 내 스트림 인덱스</summary>
    public int StreamIndex { get; init; }

    /// <summary>코덱 단축 이름 (예: "h264", "hevc", "vp9")</summary>
    public string CodecName { get; init; } = string.Empty;

    /// <summary>코덱 전체 이름 (예: "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10")</summary>
    public string CodecLongName { get; init; } = string.Empty;

    /// <summary>코덱 프로파일 이름 (예: "High", "Main", null이면 알 수 없음)</summary>
    public string? Profile { get; init; }

    /// <summary>픽셀 너비</summary>
    public int Width { get; init; }

    /// <summary>픽셀 높이</summary>
    public int Height { get; init; }

    /// <summary>초당 프레임 수 (fps). 정보 없으면 0.</summary>
    public double FrameRate { get; init; }

    /// <summary>픽셀 포맷 이름 (예: "yuv420p", "nv12")</summary>
    public string PixelFormat { get; init; } = string.Empty;

    /// <summary>스트림 비트레이트 (bps). 정보 없으면 0.</summary>
    public long BitRate { get; init; }

    /// <summary>스트림 재생 길이. 알 수 없으면 <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>"너비x높이" 형식의 해상도 문자열</summary>
    public string ResolutionText => $"{Width}×{Height}";

    /// <summary>비트레이트를 Mbps 단위로 반환합니다. 정보 없으면 null.</summary>
    public double? BitRateMbps => BitRate > 0 ? BitRate / 1_000_000.0 : null;

    /// <summary>초당 프레임 수를 소수 1자리 문자열로 반환합니다.</summary>
    public string FrameRateText => FrameRate > 0 ? $"{FrameRate:F2} fps" : "N/A";
}
