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
/// <c>&lt;font color="#RRGGBB"&gt;</c> 태그에서 색상을 추출하고,
/// 이후 모든 HTML/ASS 태그를 제거합니다.
/// </remarks>
public sealed class SrtParser : ISubtitleParser
{
    // 00:00:01,000 --> 00:00:04,000  (쉼표 또는 마침표 모두 허용)
    private static readonly Regex TimestampRe = new(
        @"(\d{1,2}:\d{2}:\d{2}[,\.]\d{3})\s*-->\s*(\d{1,2}:\d{2}:\d{2}[,\.]\d{3})",
        RegexOptions.Compiled);

    // <font color="#RRGGBB"> 또는 <font color='#RRGGBB'> 또는 <font color=#RRGGBB>
    private static readonly Regex FontColorRe = new(
        @"<font[^>]*\scolor=[""']?([#\w]+)[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // <i>, <b>, {\\an8}, <font ...> 등 HTML·ASS 태그 제거
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

            var m     = TimestampRe.Match(lines[tsIdx]);
            var start = ParseTimestamp(m.Groups[1].Value);
            var end   = ParseTimestamp(m.Groups[2].Value);

            var rawText = string.Join("\n", lines[(tsIdx + 1)..]).Trim();

            // <font color="..."> 태그에서 색상 추출 (태그 제거 전)
            string? color = null;
            var colorMatch = FontColorRe.Match(rawText);
            if (colorMatch.Success)
                color = NormalizeColor(colorMatch.Groups[1].Value);

            // 모든 HTML/ASS 태그 제거
            var text = TagRe.Replace(rawText, "");
            if (string.IsNullOrWhiteSpace(text)) continue;

            entries.Add(new SubtitleEntry(start, end, text, color));
        }

        entries.Sort((a, b) => a.Start.CompareTo(b.Start));
        return entries;
    }

    // "HH:MM:SS,mmm" 또는 "HH:MM:SS.mmm" → TimeSpan
    private static TimeSpan ParseTimestamp(string ts)
    {
        ts = ts.Trim().Replace(',', '.');
        return TimeSpan.TryParse(ts, out var result) ? result : TimeSpan.Zero;
    }

    /// <summary>색상 코드를 "#RRGGBB" 형식으로 정규화합니다.</summary>
    private static string? NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim().TrimStart('#');

        // 3자리 → 6자리 확장
        if (raw.Length == 3)
            raw = $"{raw[0]}{raw[0]}{raw[1]}{raw[1]}{raw[2]}{raw[2]}";

        if (raw.Length == 6 && IsHex(raw))
            return "#" + raw.ToUpperInvariant();

        return null;
    }

    private static bool IsHex(string s)
        => s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
}
