using System.Text.RegularExpressions;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Parsers;

/// <summary>
/// SubRip (.srt) 형식 자막 파서.
/// </summary>
/// <remarks>
/// 각 블록 구조:
/// <code>
/// 1
/// 00:00:01,000 --> 00:00:04,000
/// 자막 텍스트
/// </code>
/// </remarks>
public sealed class SrtParser : ISubtitleParser
{
    // 00:00:01,000 --> 00:00:04,000  (쉼표 또는 마침표 모두 허용)
    private static readonly Regex TimestampRe = new(
        @"(\d{1,2}:\d{2}:\d{2}[,\.]\d{3})\s*-->\s*(\d{1,2}:\d{2}:\d{2}[,\.]\d{3})",
        RegexOptions.Compiled);

    // <i>, <b>, {\\an8} 등 HTML·ASS 태그 제거
    private static readonly Regex TagRe = new(@"<[^>]+>|\{[^}]+\}", RegexOptions.Compiled);

    public IReadOnlyList<SubtitleEntry> Parse(string content)
    {
        var entries = new List<SubtitleEntry>();
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');

        foreach (var block in Regex.Split(normalized, @"\n{2,}"))
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;

            // 타임스탬프 줄 탐색 (번호 줄 다음에 있음)
            int tsIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (TimestampRe.IsMatch(lines[i])) { tsIdx = i; break; }
            }
            if (tsIdx < 0 || tsIdx + 1 >= lines.Length) continue;

            var m = TimestampRe.Match(lines[tsIdx]);
            var start = ParseTimestamp(m.Groups[1].Value);
            var end   = ParseTimestamp(m.Groups[2].Value);

            var text = string.Join("\n", lines[(tsIdx + 1)..]).Trim();
            text = TagRe.Replace(text, "");
            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new SubtitleEntry(start, end, text));
        }

        entries.Sort((a, b) => a.Start.CompareTo(b.Start));
        return entries;
    }

    // "HH:MM:SS,mmm" 또는 "HH:MM:SS.mmm" → TimeSpan
    private static TimeSpan ParseTimestamp(string ts)
    {
        // 쉼표를 마침표로 바꾸면 TimeSpan.TryParse 가 인식
        ts = ts.Trim().Replace(',', '.');
        return TimeSpan.TryParse(ts, out var result) ? result : TimeSpan.Zero;
    }
}
