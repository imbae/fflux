namespace fflux.Core.Models;

/// <summary>FFmpeg 커맨드 빌더가 사용하는 옵션 모델.</summary>
public sealed class FFmpegCommandOptions
{
    // ── 파일 ────────────────────────────────────────────────────────
    public string InputFile  { get; set; } = "";
    public string OutputFile { get; set; } = "";

    // ── 비디오 ──────────────────────────────────────────────────────
    /// <summary>null이면 -c:v 미지정, "copy"이면 스트림 복사, 그 외 코덱명.</summary>
    public string? VideoCodec   { get; set; }
    /// <summary>kbps. CRF가 지정된 경우 무시됨.</summary>
    public int?    VideoBitrate { get; set; }
    public double? Fps          { get; set; }
    /// <summary>"1920x1080" 형식. null이면 원본 해상도.</summary>
    public string? Resolution   { get; set; }
    /// <summary>0-51 (H.264/H.265). null 또는 0이면 미사용.</summary>
    public int?    Crf          { get; set; }

    // ── 오디오 ──────────────────────────────────────────────────────
    public string? AudioCodec      { get; set; }
    /// <summary>kbps.</summary>
    public int?    AudioBitrate    { get; set; }
    public int?    AudioSampleRate { get; set; }
    public int?    AudioChannels   { get; set; }

    // ── 필터 ────────────────────────────────────────────────────────
    public string? VideoFilter { get; set; }
    public string? AudioFilter { get; set; }

    // ── 고급 ────────────────────────────────────────────────────────
    /// <summary>사용자가 직접 입력한 추가 인수 (출력 파일 직전에 삽입).</summary>
    public string? ExtraArgs { get; set; }
}
