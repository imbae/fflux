namespace fflux.Core.Models;

/// <summary>ffprobe 비트레이트 분석의 완전한 결과입니다.</summary>
public sealed record BitrateAnalysisResult
{
    // ── 비트레이트 시계열 ────────────────────────────────────────────

    /// <summary>초당 비디오 비트레이트 목록 (시간 오름차순)</summary>
    public IReadOnlyList<BitratePoint> VideoPerSecond { get; init; } = [];

    /// <summary>초당 오디오 비트레이트 목록 (시간 오름차순)</summary>
    public IReadOnlyList<BitratePoint> AudioPerSecond { get; init; } = [];

    // ── 비디오 요약 통계 ─────────────────────────────────────────────

    public double AverageVideoKbps { get; init; }
    public double PeakVideoKbps    { get; init; }
    public double MinVideoKbps     { get; init; }

    // ── 프레임 타입 카운트 ───────────────────────────────────────────

    public long IFrameCount { get; init; }
    public long PFrameCount { get; init; }
    public long BFrameCount { get; init; }
    public long TotalFrames => IFrameCount + PFrameCount + BFrameCount;

    // ── 기타 ─────────────────────────────────────────────────────────

    /// <summary>분석된 미디어의 전체 재생 길이</summary>
    public TimeSpan Duration { get; init; }

    // ── 편의 프로퍼티 ────────────────────────────────────────────────

    /// <summary>I-frame 비율 문자열 (예: "I:12%  P:54%  B:34%")</summary>
    public string FrameTypeRatioText
    {
        get
        {
            if (TotalFrames == 0) return "N/A";
            double total = TotalFrames;
            return $"I:{IFrameCount / total * 100:F0}%  " +
                   $"P:{PFrameCount / total * 100:F0}%  " +
                   $"B:{BFrameCount / total * 100:F0}%";
        }
    }

    /// <summary>평균 GoP 크기 (총 프레임 / I-프레임 수)</summary>
    public string AvgGopSizeText =>
        IFrameCount > 0 ? $"{TotalFrames / (double)IFrameCount:F1}" : "N/A";
}
