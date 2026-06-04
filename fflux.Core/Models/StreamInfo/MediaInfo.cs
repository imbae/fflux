using System.Collections.ObjectModel;

namespace fflux.Core.Models.StreamInfo;

/// <summary>
/// 미디어 파일 전체의 메타데이터 정보입니다.
/// ffmpeg.autogen의 <c>AVFormatContext</c>를 기반으로 구성됩니다.
/// </summary>
public sealed record MediaInfo
{
    // ── 파일 정보 ────────────────────────────────────────────────────

    /// <summary>미디어 파일의 전체 경로</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>파일명 (확장자 포함)</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>파일 크기 (바이트)</summary>
    public long FileSize { get; init; }

    // ── 컨테이너 정보 ────────────────────────────────────────────────

    /// <summary>컨테이너 포맷 단축명 (예: "matroska,webm", "mp4")</summary>
    public string FormatName { get; init; } = string.Empty;

    /// <summary>컨테이너 포맷 전체명 (예: "Matroska / WebM")</summary>
    public string FormatLongName { get; init; } = string.Empty;

    /// <summary>미디어 전체 재생 길이</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>컨테이너 전체 비트레이트 (bps). 정보 없으면 0.</summary>
    public long BitRate { get; init; }

    // ── 스트림 목록 ─────────────────────────────────────────────────

    /// <summary>비디오 스트림 목록 (대부분 1개, 가끔 여러 개)</summary>
    public ReadOnlyCollection<VideoStreamInfo> VideoStreams { get; init; }
        = new([]);

    /// <summary>오디오 스트림 목록 (다국어 트랙 등)</summary>
    public ReadOnlyCollection<AudioStreamInfo> AudioStreams { get; init; }
        = new([]);

    /// <summary>자막 스트림 목록</summary>
    public ReadOnlyCollection<SubtitleStreamInfo> SubtitleStreams { get; init; }
        = new([]);

    // ── 편의 프로퍼티 ────────────────────────────────────────────────

    /// <summary>파일 크기를 사람이 읽기 쉬운 형식으로 반환합니다 (예: "1.23 GB").</summary>
    public string FileSizeText => FileSize switch
    {
        >= 1_073_741_824 => $"{FileSize / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{FileSize / 1_048_576.0:F2} MB",
        >= 1_024         => $"{FileSize / 1_024.0:F1} KB",
        _                => $"{FileSize} B"
    };

    /// <summary>전체 비트레이트를 Mbps 단위 문자열로 반환합니다.</summary>
    public string? BitRateText => BitRate > 0 ? $"{BitRate / 1_000_000.0:F2} Mbps" : null;

    /// <summary>재생 시간을 "HH:MM:SS" 형식 문자열로 반환합니다.</summary>
    public string DurationText => Duration == TimeSpan.Zero
        ? "N/A"
        : $"{(int)Duration.TotalHours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}";

    /// <summary>주 비디오 스트림 (첫 번째 스트림). 없으면 null.</summary>
    public VideoStreamInfo? PrimaryVideo => VideoStreams.Count > 0 ? VideoStreams[0] : null;

    /// <summary>주 오디오 스트림 (첫 번째 스트림). 없으면 null.</summary>
    public AudioStreamInfo? PrimaryAudio => AudioStreams.Count > 0 ? AudioStreams[0] : null;
}
