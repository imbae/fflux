using System.Diagnostics;
using System.Globalization;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Services;

/// <summary>
/// ffprobe의 <c>-show_entries frame</c> / <c>-show_entries packet</c>를 이용해
/// 초당 비트레이트와 프레임 타입 분포를 분석하는 서비스 구현체.
/// </summary>
public sealed class BitrateAnalysisService : IBitrateAnalysisService
{
    /// <inheritdoc/>
    public async Task<BitrateAnalysisResult> AnalyzeAsync(
        string                                         ffprobeExePath,
        string                                         inputFile,
        IProgress<(string Stage, int FramesProcessed)> progress,
        CancellationToken                              ct = default)
    {
        // ── ① 비디오 프레임 분석 ─────────────────────────────────────
        progress.Report(("비디오 프레임 분석 중…", 0));

        var frames = await ReadVideoFramesAsync(ffprobeExePath, inputFile, progress, ct)
            .ConfigureAwait(false);

        // ── ② 오디오 패킷 분석 ──────────────────────────────────────
        progress.Report(("오디오 패킷 분석 중…", frames.Count));

        var audioPackets = await ReadAudioPacketsAsync(ffprobeExePath, inputFile, ct)
            .ConfigureAwait(false);

        // ── ③ 결과 집계 ─────────────────────────────────────────────
        return BuildResult(frames, audioPackets);
    }

    // ── 비디오 프레임 읽기 ──────────────────────────────────────────

    private static async Task<List<(double pts, long size, char type)>> ReadVideoFramesAsync(
        string ffprobe,
        string input,
        IProgress<(string, int)> progress,
        CancellationToken ct)
    {
        // FFmpeg 5.0+ 에서 pkt_pts_time이 deprecated → pts_time 사용
        // 폴백: best_effort_timestamp_time (pts가 N/A인 경우)
        // CSV 출력: pts_time,best_effort_timestamp_time,pkt_size,pict_type
        var args =
            $"-v error -select_streams v:0 " +
            $"-show_entries frame=pts_time,best_effort_timestamp_time,pkt_size,pict_type " +
            $"-of csv=p=0 \"{input}\"";

        var psi = new ProcessStartInfo(ffprobe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        var results = new List<(double, long, char)>();
        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // stderr는 무시 (진행에 방해 안 되도록 비동기 드레인)
        _ = proc.StandardError.ReadToEndAsync(ct);

        string? line;
        int count = 0;
        while ((line = await proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (TryParseVideoFrame(line, out var entry))
            {
                results.Add(entry);
                count++;
                if (count % 200 == 0)
                    progress.Report(("비디오 프레임 분석 중…", count));
            }
        }

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        // 최종 카운트 업데이트
        progress.Report(("비디오 프레임 분석 완료", count));
        return results;
    }

    private static bool TryParseVideoFrame(
        string line,
        out (double pts, long size, char type) result)
    {
        result = default;
        // 출력 필드: pts_time, best_effort_timestamp_time, pkt_size, pict_type
        var parts = line.Split(',');
        if (parts.Length < 4) return false;

        // pts_time 또는 best_effort_timestamp_time 중 유효한 값 사용
        double pts = double.MinValue;
        if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out pts) || pts < 0)
        {
            if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out pts) || pts < 0)
                return false;
        }

        if (!long.TryParse(parts[2], out long size) || size <= 0) return false;

        char type = parts[3].Length > 0 ? char.ToUpperInvariant(parts[3][0]) : '?';
        result = (pts, size, type);
        return true;
    }

    // ── 오디오 패킷 읽기 ────────────────────────────────────────────

    private static async Task<List<(double pts, long size)>> ReadAudioPacketsAsync(
        string ffprobe,
        string input,
        CancellationToken ct)
    {
        // CSV 출력: pts_time,size
        var args =
            $"-v quiet -select_streams a:0 " +
            $"-show_entries packet=pts_time,size " +
            $"-of csv=p=0 \"{input}\"";

        var psi = new ProcessStartInfo(ffprobe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        var results = new List<(double, long)>();
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        _ = proc.StandardError.ReadToEndAsync(ct);

        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0], NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double pts)) continue;
            if (!long.TryParse(parts[1], out long size)) continue;
            results.Add((pts, size));
        }

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return results;
    }

    // ── 결과 집계 ───────────────────────────────────────────────────

    private static BitrateAnalysisResult BuildResult(
        List<(double pts, long size, char type)> frames,
        List<(double pts, long size)>            audioPackets)
    {
        if (frames.Count == 0)
            return new BitrateAnalysisResult();

        // 초당 비디오 비트레이트 집계
        var videoPerSecond = frames
            .Where(f => f.pts >= 0)
            .GroupBy(f => (int)f.pts)
            .OrderBy(g => g.Key)
            .Select(g => new BitratePoint(
                TimeSeconds:  g.Key,
                BitrateKbps:  g.Sum(f => f.size) * 8.0 / 1000.0))
            .ToList()
            .AsReadOnly();

        // 초당 오디오 비트레이트 집계
        var audioPerSecond = audioPackets
            .Where(p => p.pts >= 0)
            .GroupBy(p => (int)p.pts)
            .OrderBy(g => g.Key)
            .Select(g => new BitratePoint(
                TimeSeconds:  g.Key,
                BitrateKbps:  g.Sum(p => p.size) * 8.0 / 1000.0))
            .ToList()
            .AsReadOnly();

        long iCount = frames.LongCount(f => f.type == 'I');
        long pCount = frames.LongCount(f => f.type == 'P');
        long bCount = frames.LongCount(f => f.type == 'B');

        double avgKbps  = videoPerSecond.Count > 0 ? videoPerSecond.Average(p => p.BitrateKbps) : 0;
        double peakKbps = videoPerSecond.Count > 0 ? videoPerSecond.Max(p => p.BitrateKbps) : 0;
        double minKbps  = videoPerSecond.Count > 0 ? videoPerSecond.Min(p => p.BitrateKbps) : 0;
        double maxPts   = frames.Max(f => f.pts);

        return new BitrateAnalysisResult
        {
            VideoPerSecond   = videoPerSecond,
            AudioPerSecond   = audioPerSecond,
            AverageVideoKbps = avgKbps,
            PeakVideoKbps    = peakKbps,
            MinVideoKbps     = minKbps,
            IFrameCount      = iCount,
            PFrameCount      = pCount,
            BFrameCount      = bCount,
            Duration         = TimeSpan.FromSeconds(maxPts),
        };
    }
}
