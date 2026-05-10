namespace fflux.UI.Modules.SceneDetector;

/// <summary>
/// DataGrid에서 제목을 직접 편집할 수 있는 챕터 항목 ViewModel.
/// </summary>
public sealed partial class ChapterEntry : ObservableObject
{
    // ── 읽기 전용 데이터 ─────────────────────────────────────────────

    /// <summary>챕터 번호 (1부터 시작)</summary>
    public int Number { get; }

    /// <summary>챕터 시작 타임스탬프</summary>
    public TimeSpan Start { get; }

    /// <summary>FFmpeg scene_score (0 이면 알 수 없음)</summary>
    public double Score { get; }

    // ── 편집 가능 필드 ────────────────────────────────────────────────

    [ObservableProperty] private string _title;

    // ── 표시용 계산 프로퍼티 ─────────────────────────────────────────

    /// <summary>"HH:MM:SS" 형식 타임스탬프</summary>
    public string TimestampText =>
        $"{(int)Start.TotalHours:D2}:{Start.Minutes:D2}:{Start.Seconds:D2}";

    /// <summary>소수 세 자리 점수 문자열. 0 이면 "—" 반환.</summary>
    public string ScoreText => Score > 0 ? Score.ToString("F3") : "—";

    // ── 생성자 ───────────────────────────────────────────────────────

    public ChapterEntry(int number, TimeSpan start, double score)
    {
        Number = number;
        Start  = start;
        Score  = score;
        _title = $"Chapter {number}";
    }
}
