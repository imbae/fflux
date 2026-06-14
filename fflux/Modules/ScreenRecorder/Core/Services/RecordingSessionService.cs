using System.Diagnostics;
using fflux.Core.Abstractions;
using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

/// <summary>
/// 화면 캡처 + 오디오 캡처 + 인코딩을 조율하는 녹화 세션 서비스.
/// StartAsync() 는 즉시 반환되고, 캡처 루프는 별도 Task에서 실행됩니다.
/// </summary>
public sealed class RecordingSessionService : IRecordingSessionService
{
    private readonly IScreenCaptureService    _capture;
    private readonly IAudioCaptureService     _audio;
    private readonly IScreenRecordingEncoder  _encoder;
    private readonly IDrawingOverlaySource?   _drawingOverlay;
    private readonly ILogger<RecordingSessionService> _logger;

    private CancellationTokenSource? _cts;
    private Task?                    _captureTask;
    private bool                     _paused;

    private readonly Stopwatch              _sw           = new();
    private readonly System.Timers.Timer    _elapsedTimer = new(500) { AutoReset = true };

    public RecordingState State          { get; private set; } = RecordingState.Idle;
    public TimeSpan       Elapsed        => _sw.Elapsed;
    public string?        OutputFilePath { get; private set; }

    public event EventHandler<RecordingState>? StateChanged;
    public event EventHandler<TimeSpan>?       ElapsedUpdated;

    public RecordingSessionService(
        IScreenCaptureService    capture,
        IAudioCaptureService     audio,
        IScreenRecordingEncoder  encoder,
        ILogger<RecordingSessionService> logger,
        IDrawingOverlaySource?   drawingOverlay = null)
    {
        _capture        = capture;
        _audio          = audio;
        _encoder        = encoder;
        _logger         = logger;
        _drawingOverlay = drawingOverlay;

        _elapsedTimer.Elapsed += (_, _) => ElapsedUpdated?.Invoke(this, _sw.Elapsed);
    }

    // ── 공개 API ─────────────────────────────────────────────────────

    public Task StartAsync(RecordingSettings settings, CancellationToken ct = default)
    {
        if (State != RecordingState.Idle)
            throw new InvalidOperationException("이미 녹화 중입니다.");

        // 출력 파일명 자동 생성
        string fileName   = $"fflux_rec_{DateTime.Now:yyyyMMdd_HHmmss}.mkv";
        OutputFilePath    = Path.Combine(settings.OutputDirectory, fileName);
        Directory.CreateDirectory(settings.OutputDirectory);
        _paused = false;

        // 캡처 대상 설정
        _capture.SetCaptureTarget(
            new CaptureTarget(settings.CaptureTarget.Mode, settings.CaptureTarget.MonitorIndex));
        _capture.SetTargetFps(settings.Fps);
        if (settings.CaptureRegion.HasValue)
            _capture.SetCaptureRegion(settings.CaptureRegion.Value);

        // 오디오 설정
        _audio.SystemAudioEnabled = settings.RecordSystemAudio;
        _audio.MicrophoneEnabled  = settings.RecordMicrophone;

        // 오디오 이벤트 구독 → 인코더로 전달
        _audio.DataAvailable += OnAudioData;

        // 오디오 캡처 시작
        if (settings.RecordSystemAudio || settings.RecordMicrophone)
            _audio.Start();

        // 비디오 캡처 루프 (별도 Task)
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _captureTask = Task.Run(
            () => RunVideoLoopAsync(settings, _cts.Token), CancellationToken.None);

        SetState(RecordingState.Recording);
        _sw.Restart();
        _elapsedTimer.Start();

        _logger.LogInformation("녹화 시작: {Path}", OutputFilePath);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (State != RecordingState.Recording) return Task.CompletedTask;
        _paused = true;
        _sw.Stop();
        SetState(RecordingState.Paused);
        _logger.LogInformation("녹화 일시정지");
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (State != RecordingState.Paused) return Task.CompletedTask;
        _paused = false;
        _sw.Start();
        SetState(RecordingState.Recording);
        _logger.LogInformation("녹화 재개");
        return Task.CompletedTask;
    }

    public async Task<string?> StopAsync()
    {
        if (State == RecordingState.Idle) return null;

        SetState(RecordingState.Stopping);
        _elapsedTimer.Stop();
        _sw.Stop();

        // 비디오 캡처 루프 중지
        _cts?.Cancel();
        if (_captureTask is not null)
        {
            try { await _captureTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "캡처 루프 종료 중 예외"); }
        }

        // 오디오 중지
        _audio.DataAvailable -= OnAudioData;
        _audio.Stop();

        // 인코더 닫기 (flush + trailer)
        if (_encoder.IsOpen)
            _encoder.Close();

        SetState(RecordingState.Idle);
        _logger.LogInformation("녹화 완료: {Path}", OutputFilePath);
        return OutputFilePath;
    }

    // ── 비디오 캡처 루프 ─────────────────────────────────────────────

    private async Task RunVideoLoopAsync(RecordingSettings settings, CancellationToken ct)
    {
        bool encoderOpened = false;

        try
        {
            await foreach (var frame in _capture.CaptureAsync(ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;
                if (_paused) continue;

                // 첫 프레임에서 인코더 초기화 (실제 캡처 해상도 기준)
                if (!encoderOpened)
                {
                    bool hasAudio = settings.RecordSystemAudio || settings.RecordMicrophone;
                    int  audioRate = hasAudio ? _audio.SampleRate : 0;
                    int  audioCh   = hasAudio ? _audio.Channels   : 0;

                    try
                    {
                        _encoder.Open(OutputFilePath!, frame.Width, frame.Height,
                                      settings.Fps, audioRate, audioCh);
                        encoderOpened = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "인코더 초기화 실패 — 녹화 중단");
                        break;
                    }
                }

                byte[] frameData = frame.Data;
                if (_drawingOverlay?.HasVisibleContent == true)
                {
                    var overlay = await _drawingOverlay.GetPixelsAsync(frame.Width, frame.Height)
                                                       .ConfigureAwait(false);
                    if (overlay is not null)
                        frameData = CompositeFrames(frame.Data, overlay, frame.Width, frame.Height);
                }

                _encoder.WriteVideoFrame(frameData);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "비디오 캡처 루프 오류");
        }
    }

    /// <summary>
    /// BGRA32 캡처 프레임 위에 PBGRA32 그리기 오버레이를 알파 블렌딩으로 합성합니다.
    /// </summary>
    private static byte[] CompositeFrames(byte[] bgra, byte[] pbgra, int w, int h)
    {
        var result = (byte[])bgra.Clone();
        int len    = w * h * 4;
        for (int i = 0; i < len; i += 4)
        {
            byte a = pbgra[i + 3];
            if (a == 0) continue;
            float inv = (255 - a) / 255f;
            // PBGRA: 채널이 이미 alpha와 곱해져 있음
            result[i]     = (byte)Math.Min(255, pbgra[i]     + result[i]     * inv);
            result[i + 1] = (byte)Math.Min(255, pbgra[i + 1] + result[i + 1] * inv);
            result[i + 2] = (byte)Math.Min(255, pbgra[i + 2] + result[i + 2] * inv);
        }
        return result;
    }

    private void OnAudioData(object? sender, AudioDataEventArgs e)
    {
        if (!_encoder.IsOpen || _paused) return;
        try
        {
            _encoder.WriteAudioSamples(
                e.Buffer[..e.BytesRecorded],
                e.SampleRate, e.Channels, e.IsFloat32);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "오디오 기록 실패 (무시)");
        }
    }

    private void SetState(RecordingState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        _elapsedTimer.Dispose();
        if (State != RecordingState.Idle)
            await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
