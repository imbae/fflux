using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Services;

/// <summary>
/// FFmpeg의 <c>select</c> + <c>metadata=print</c> 필터를 이용해
/// 장면 전환을 감지하는 서비스 구현체.
/// </summary>
public sealed class SceneDetectionService : ISceneDetectionService
{
    // ── 정규식 ──────────────────────────────────────────────────────

    // "Duration: HH:MM:SS.cc"
    private static readonly Regex DurationRe = new(
        @"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d{2})",
        RegexOptions.Compiled);

    // metadata=print 출력: "frame:N    pts:N       pts_time:T.TTTTT"
    private static readonly Regex PtsTimeRe = new(
        @"pts_time:([0-9]+(?:\.[0-9]*)?)",
        RegexOptions.Compiled);

    // metadata=print 출력: "lavfi.scene_score=S.SSSSS"
    private static readonly Regex ScoreRe = new(
        @"scene_score=([0-9]+(?:\.[0-9]*)?)",
        RegexOptions.Compiled);

    // ── ISceneDetectionService 구현 ─────────────────────────────────

    /// <inheritdoc/>
    public async Task<SceneDetectionResult> DetectScenesAsync(
        string                            ffmpegExePath,
        string                            inputFile,
        double                            threshold,
        IProgress<SceneDetectionProgress> progress,
        CancellationToken                 ct = default)
    {
        var thrStr = threshold.ToString("F3", CultureInfo.InvariantCulture);
        var vf     = $"select='gt(scene,{thrStr})',metadata=print";
        var args   = $"-hide_banner -i \"{inputFile}\" -vf \"{vf}\" -an -f null -";

        var psi = new ProcessStartInfo(ffmpegExePath, args)
        {
            UseShellExecute       = false,
            RedirectStandardError = true,
            CreateNoWindow        = true,
        };

        var scenes        = new List<SceneEntry>();
        TimeSpan? total   = null;
        TimeSpan? pending = null;   // 직전에 파싱된 pts_time

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.ErrorDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line is null) return;

            // ① 전체 길이 (최초 1회)
            if (total is null)
            {
                var dm = DurationRe.Match(line);
                if (dm.Success && TimeSpan.TryParse(dm.Groups[1].Value, out var d))
                    total = d;
            }

            // ② pts_time → 장면 전환 후보 타임스탬프
            var pm = PtsTimeRe.Match(line);
            if (pm.Success && double.TryParse(
                    pm.Groups[1].Value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double secs))
            {
                pending = TimeSpan.FromSeconds(secs);
            }

            // ③ scene_score → 직전 pts_time과 쌍을 이루면 SceneEntry 생성
            var sm = ScoreRe.Match(line);
            if (sm.Success && pending.HasValue
                && double.TryParse(sm.Groups[1].Value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double score))
            {
                var entry = new SceneEntry(pending.Value, score);
                lock (scenes) scenes.Add(entry);
                pending = null;

                progress.Report(new SceneDetectionProgress
                {
                    ScenesFound     = scenes.Count,
                    LatestSceneTime = entry.Timestamp,
                });
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 이미 종료됨 */ }
            throw;
        }

        List<SceneEntry> snapshot;
        lock (scenes) snapshot = [.. scenes];

        var sorted = snapshot
            .OrderBy(s => s.Timestamp)
            .ToList()
            .AsReadOnly();

        return new SceneDetectionResult(sorted, total ?? TimeSpan.Zero);
    }
}
