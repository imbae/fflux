namespace fflux.Core.Models;

/// <summary>FFmpeg 프로세스 실행 중 보고되는 진행률 정보.</summary>
public sealed class FFmpegProgress
{
    /// <summary>0~100 진행률. 총 길이를 알 수 없으면 null.</summary>
    public double?   Percent     { get; init; }

    /// <summary>현재 처리 시각.</summary>
    public TimeSpan? CurrentTime { get; init; }

    /// <summary>stderr 로그 한 줄.</summary>
    public string?   LogLine     { get; init; }
}
