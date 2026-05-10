namespace fflux.Core.Models;

/// <summary>장면 감지 진행 중 보고되는 상태 정보입니다.</summary>
public sealed class SceneDetectionProgress
{
    /// <summary>지금까지 발견된 장면 전환 수</summary>
    public int ScenesFound { get; init; }

    /// <summary>가장 최근에 감지된 장면 전환 타임스탬프. 아직 없으면 null.</summary>
    public TimeSpan? LatestSceneTime { get; init; }
}
