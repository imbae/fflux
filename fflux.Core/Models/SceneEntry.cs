namespace fflux.Core.Models;

/// <summary>장면 전환이 감지된 지점의 정보입니다.</summary>
public sealed record SceneEntry(
    /// <summary>장면 전환이 발생한 타임스탬프</summary>
    TimeSpan Timestamp,
    /// <summary>FFmpeg select 필터의 scene 점수 (0.0 ~ 1.0). 알 수 없으면 0.</summary>
    double Score);
