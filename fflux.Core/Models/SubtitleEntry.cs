namespace fflux.Core.Models;

/// <summary>
/// 자막 항목 하나 (시작/종료 시각 + 텍스트 + 선택적 색상).
/// <para>
/// <paramref name="Color"/>는 "#RRGGBB" 형식의 색상 코드이며,
/// SRT 파싱 시 <c>&lt;font color="..."&gt;</c> 태그에서 추출됩니다.
/// null 이면 기본 색상(흰색)을 사용합니다.
/// </para>
/// </summary>
public record SubtitleEntry(TimeSpan Start, TimeSpan End, string Text, string? Color = null);
