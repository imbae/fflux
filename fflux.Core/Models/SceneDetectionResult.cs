namespace fflux.Core.Models;

/// <summary>장면 감지 완료 결과입니다.</summary>
public sealed record SceneDetectionResult(
    /// <summary>감지된 장면 전환 목록 (타임스탬프 오름차순 정렬)</summary>
    IReadOnlyList<SceneEntry> Scenes,
    /// <summary>미디어 전체 재생 길이. 알 수 없으면 Zero.</summary>
    TimeSpan TotalDuration);
