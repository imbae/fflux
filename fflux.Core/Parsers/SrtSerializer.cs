using System.Text;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Parsers;

/// <summary>
/// SubRip (.srt) 형식으로 자막을 직렬화합니다.
/// <para>
/// 출력 예시:
/// <code>
/// 1
/// 00:00:01,000 --> 00:00:04,000
/// Hello World
///
/// 2
/// 00:00:05,000 --> 00:00:08,000
/// Second subtitle
/// </code>
/// </para>
/// </summary>
public sealed class SrtSerializer : ISubtitleSerializer
{
    /// <inheritdoc/>
    public string Serialize(IEnumerable<SubtitleEntry> entries)
    {
        var sb = new StringBuilder();
        int index = 1;

        foreach (var entry in entries.OrderBy(e => e.Start))
        {
            if (sb.Length > 0) sb.AppendLine();

            sb.AppendLine(index.ToString());
            sb.AppendLine($"{FormatTimestamp(entry.Start)} --> {FormatTimestamp(entry.End)}");

            // 색상이 지정된 경우 <font color="..."> 태그로 감쌉니다.
            if (!string.IsNullOrWhiteSpace(entry.Color))
                sb.AppendLine($"<font color=\"{entry.Color}\">{entry.Text}</font>");
            else
                sb.AppendLine(entry.Text);

            index++;
        }

        return sb.ToString();
    }

    // "HH:MM:SS,mmm" — SRT 표준 형식
    private static string FormatTimestamp(TimeSpan ts)
        => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
}
