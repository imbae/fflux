using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using fflux.Core.Abstractions;
using fflux.Core.Models;

namespace fflux.Core.Services;

/// <summary>
/// FFmpeg 커맨드 빌더 및 프로세스 실행 구현체.
/// </summary>
public sealed class FFmpegCommandService : IFFmpegCommandService
{
    // stderr에서 총 길이 파싱: "Duration: HH:MM:SS.ss"
    private static readonly Regex DurationRe = new(
        @"Duration:\s*(\d{2}:\d{2}:\d{2}\.\d{2})",
        RegexOptions.Compiled);

    // stderr에서 현재 위치 파싱: "time=HH:MM:SS.ss"
    private static readonly Regex TimeRe = new(
        @"time=(\d{2}:\d{2}:\d{2}\.\d{2})",
        RegexOptions.Compiled);

    // ── 커맨드 빌더 ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public string BuildArguments(FFmpegCommandOptions o)
    {
        var sb = new StringBuilder("-y");

        // 입력
        if (!string.IsNullOrEmpty(o.InputFile))
            sb.Append($" -i \"{o.InputFile}\"");

        // 비디오
        if (o.VideoCodec == "copy")
        {
            sb.Append(" -c:v copy");
        }
        else if (o.VideoCodec != null)
        {
            sb.Append($" -c:v {o.VideoCodec}");

            // CRF 우선, 없으면 비트레이트
            if (o.Crf is > 0)
                sb.Append($" -crf {o.Crf}");
            else if (o.VideoBitrate is > 0)
                sb.Append($" -b:v {o.VideoBitrate}k");

            if (o.Fps is > 0)
                sb.Append(FormattableString.Invariant($" -r {o.Fps}"));

            if (!string.IsNullOrEmpty(o.Resolution))
                sb.Append($" -s {o.Resolution}");
        }

        // 비디오 필터
        if (!string.IsNullOrWhiteSpace(o.VideoFilter))
            sb.Append($" -vf \"{o.VideoFilter.Trim()}\"");

        // 오디오
        if (o.AudioCodec == "copy")
        {
            sb.Append(" -c:a copy");
        }
        else if (o.AudioCodec != null)
        {
            sb.Append($" -c:a {o.AudioCodec}");

            if (o.AudioBitrate is > 0)
                sb.Append($" -b:a {o.AudioBitrate}k");

            if (o.AudioSampleRate is > 0)
                sb.Append($" -ar {o.AudioSampleRate}");

            if (o.AudioChannels is > 0)
                sb.Append($" -ac {o.AudioChannels}");
        }

        // 오디오 필터
        if (!string.IsNullOrWhiteSpace(o.AudioFilter))
            sb.Append($" -af \"{o.AudioFilter.Trim()}\"");

        // 고급 추가 인수
        if (!string.IsNullOrWhiteSpace(o.ExtraArgs))
            sb.Append($" {o.ExtraArgs.Trim()}");

        // 출력
        if (!string.IsNullOrEmpty(o.OutputFile))
            sb.Append($" \"{o.OutputFile}\"");

        return sb.ToString().Trim();
    }

    // ── 프로세스 실행 ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<int> ExecuteAsync(
        string                    ffmpegExePath,
        FFmpegCommandOptions      options,
        IProgress<FFmpegProgress> progress,
        CancellationToken         ct = default)
    {
        var args = BuildArguments(options);

        var psi = new ProcessStartInfo(ffmpegExePath, args)
        {
            UseShellExecute       = false,
            RedirectStandardError = true,
            CreateNoWindow        = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        TimeSpan? totalDuration = null;

        process.ErrorDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line == null) return;

            // 총 길이 파싱 (한 번만)
            if (totalDuration == null)
            {
                var dm = DurationRe.Match(line);
                if (dm.Success && TimeSpan.TryParse(dm.Groups[1].Value, out var d))
                    totalDuration = d;
            }

            // 현재 위치 파싱
            double?   percent     = null;
            TimeSpan? currentTime = null;
            var tm = TimeRe.Match(line);
            if (tm.Success && TimeSpan.TryParse(tm.Groups[1].Value, out var t))
            {
                currentTime = t;
                if (totalDuration is { TotalSeconds: > 0 } dur)
                    percent = Math.Min(100.0, t.TotalSeconds / dur.TotalSeconds * 100.0);
            }

            progress.Report(new FFmpegProgress
            {
                LogLine     = line,
                CurrentTime = currentTime,
                Percent     = percent,
            });
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

        return process.ExitCode;
    }
}
