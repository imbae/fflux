namespace fflux.Core.Models.StreamInfo;

/// <summary>자막 스트림의 메타데이터 정보입니다.</summary>
public sealed record SubtitleStreamInfo
{
    /// <summary>컨테이너 내 스트림 인덱스</summary>
    public int StreamIndex { get; init; }

    /// <summary>코덱 단축 이름 (예: "ass", "subrip", "dvd_subtitle")</summary>
    public string CodecName { get; init; } = string.Empty;

    /// <summary>스트림 언어 태그 (예: "kor", "eng"). 없으면 null.</summary>
    public string? Language { get; init; }

    /// <summary>자막 트랙 제목. 없으면 null.</summary>
    public string? Title { get; init; }

    /// <summary>UI 표시용 레이블. 언어 → 제목 → "Subtitle #N" 순으로 반환합니다.</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Title)    ? Title :
        !string.IsNullOrWhiteSpace(Language) ? Language.ToUpperInvariant() :
        $"Subtitle #{StreamIndex}";
}
