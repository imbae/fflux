using System.Text.RegularExpressions;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Parsers;

/// <summary>
/// WebVTT (.vtt) 형식 자막 파서.
/// </summary>
/// <remarks>
/// HH:MM:SS.mmm 및 MM:SS.mmm 두 가지 타임스탬프 형식을 모두 지원합니다.
/// NOTE·STYLE·REGION 블록은 무시합니다.
/// </remarks>
public sealed class VttParser : ISubtitleParser
{
    // HH:MM:SS.mmm --> HH:MM:SS.mmm [settings]  또는  MM:SS.mmm --> MM:SS.mmm
    private static readonly Regex TimestampRe = new(
        @"((?:\d+:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d+:)?\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    // <i>, <b>, <00:00:01.000>, <c.yellow> 등 WebVTT 인라인 태그 제거
    private static readonly Regex TagRe = new(@"<[^>]+>|\{[^}]+\}", RegexOptions.Compiled);

    public IReadOnlyList<SubtitleEntry> Parse(string content)
    {
        var entries = new List<SubtitleEntry>();
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');

        foreach (var block in Regex.Split(normalized, @"\n{2,}"))
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // 헤더 / NOTE / STYLE / REGION 블록 스킵
            if (trimmed.StartsWith("WEBVTT", StringComparison.Ordinal)
             || trimmed.StartsWith("NOTE",   StringComparison.Ordinal)
             || trimmed.StartsWith("STYLE",  StringComparison.Ordinal)
             || trimmed.StartsWith("REGION", StringComparison.Ordinal))
                continue;

            var lines = trimmed.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;

            // 타임스탬프 줄 탐색 (큐 ID 다음 또는 첫 번째 줄)
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

    // "HH:MM:SS.mmm" 또는 "MM:SS.mmm" → TimeSpan
    private static TimeSpan ParseTimestamp(string ts)
    {
        ts = ts.Trim();
        var parts = ts.Split(':');

        return parts.Length switch
        {
            // HH:MM:SS.mmm
            3 => TimeSpan.TryParse(ts, out var r3) ? r3 : TimeSpan.Zero,

            // MM:SS.mmm (minutes can be ≥ 60 per WebVTT spec)
            2 => ParseMinutesSeconds(parts[0], parts[1]),

            _ => TimeSpan.Zero,
        };
    }

    private static TimeSpan ParseMinutesSeconds(string minutesPart, string secondsPart)
    {
        var secSplit = secondsPart.Split('.');
        if (!int.TryParse(minutesPart, out int minutes)) return TimeSpan.Zero;
        if (!int.TryParse(secSplit[0], out int seconds)) return TimeSpan.Zero;
        int ms = secSplit.Length > 1 && int.TryParse(secSplit[1].PadRight(3, '0')[..3], out int parsedMs)
            ? parsedMs : 0;
        return new TimeSpan(0, 0, minutes, seconds, ms);
    }
}
