namespace fflux.Core.Models;

/// <summary>자막 항목 하나 (시작/종료 시각 + 텍스트).</summary>
public record SubtitleEntry(TimeSpan Start, TimeSpan End, string Text);
