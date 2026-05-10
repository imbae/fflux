using System.Windows.Threading;
using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace fflux.UI.Modules.Player;

/// <summary>
/// 비디오 플레이어 ViewModel.
/// IVideoDecoder + IAudioDecoder 병렬 루프 → WriteableBitmap 렌더링 + NAudio 출력을 관리합니다.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IServiceProvider         _services;
    private readonly MainWindowViewModel      _mainVm;
    private readonly ILogger<PlayerViewModel> _logger;

    // ── FFmpeg 디코더 (파일 열기 시 교체) ────────────────────────────

    private IVideoDecoder? _videoDecoder;
    private IAudioDecoder? _audioDecoder;

    // ── 오디오 출력 (NAudio, DI 미등록 — ViewModel이 수명 관리) ─────

    private IAudioPlayer? _audioPlayer;

    // ── 재생 루프 제어 ───────────────────────────────────────────────

    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _seekDebounce;
    private Task? _videoLoopTask;
    private Task? _audioLoopTask;

    // 재생 속도 (UI 스레드에서 쓰고 백그라운드에서 읽음 — 약간의 stale read 허용)
    private double _playbackSpeed = 1.0;
    private static readonly double[] SpeedValues = [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    // 재생 루프가 PositionSeconds를 업데이트할 때 시크를 재트리거하지 않도록 막는 플래그
    private volatile bool _isUpdatingPositionFromPlayback;

    // 일시정지/중단 시 재개할 위치
    private TimeSpan _pausedPosition = TimeSpan.Zero;

    // ── 공개 프로퍼티 ────────────────────────────────────────────────

    /// <summary>현재 재생 프레임의 WriteableBitmap (BGRA 32-bit). Image.Source에 바인딩.</summary>
    public WriteableBitmap? VideoBitmap { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isFileOpen;

    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private string _timecodeText = "00:00 / 00:00";
    [ObservableProperty] private string _statusText   = "파일을 열어주세요";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeSymbol))]
    private double _volumeLevel = 80.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeSymbol))]
    private bool _isMuted;

    [ObservableProperty]
    private int _selectedSpeedIndex = 3; // 1× (SpeedValues[3])

    public SymbolRegular VolumeSymbol =>
        IsMuted || VolumeLevel == 0  ? SymbolRegular.SpeakerMute24 :
        VolumeLevel < 50             ? SymbolRegular.Speaker120 :
                                       SymbolRegular.Speaker224;

    // ── 생성자 ──────────────────────────────────────────────────────

    public PlayerViewModel(
        IServiceProvider         services,
        MainWindowViewModel      mainVm,
        ILogger<PlayerViewModel> logger)
    {
        _services = services;
        _mainVm   = mainVm;
        _logger   = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // Commands
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "미디어 파일 열기",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        await OpenFileInternalAsync(dlg.FileName);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (_videoDecoder == null || !_videoDecoder.IsOpen) return;

        // 일시정지 후 재개: 두 디코더를 중단 위치로 시크
        if (_pausedPosition > TimeSpan.Zero)
        {
            await Task.WhenAll(
                _videoDecoder.SeekAsync(_pausedPosition),
                _audioDecoder?.SeekAsync(_pausedPosition) ?? Task.CompletedTask);
            _audioPlayer?.ClearBuffer();
        }

        StartPlaybackLoop();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _pausedPosition = TimeSpan.FromSeconds(PositionSeconds);
        StopPlaybackLoop(pauseAudio: true);
        IsPlaying = false;
        UpdateStatus("일시 정지");
        _mainVm.PlaybackStatusText = "일시 정지";
        _mainVm.PlaybackStatusIcon = "Pause24";
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        _pausedPosition = TimeSpan.Zero;
        StopPlaybackLoop(pauseAudio: false);
        IsPlaying = false;

        await Task.WhenAll(
            _videoDecoder?.IsOpen == true ? _videoDecoder.SeekAsync(TimeSpan.Zero) : Task.CompletedTask,
            _audioDecoder?.IsOpen == true ? _audioDecoder.SeekAsync(TimeSpan.Zero) : Task.CompletedTask);

        _isUpdatingPositionFromPlayback = true;
        PositionSeconds = 0;
        _isUpdatingPositionFromPlayback = false;

        UpdateTimecode(TimeSpan.Zero);
        UpdateStatus("정지");
        _mainVm.PlaybackStatusText = "정지";
        _mainVm.PlaybackStatusIcon = "Stop24";
    }

    // ── CanExecute ───────────────────────────────────────────────────

    private bool CanPlay()  => IsFileOpen && !IsPlaying;
    private bool CanPause() => IsPlaying;
    private bool CanStop()  => IsFileOpen;

    // ── 볼륨 / 속도 ──────────────────────────────────────────────────

    partial void OnVolumeLevelChanged(double value)  => SyncVolume();
    partial void OnIsMutedChanged(bool value)        => SyncVolume();

    private void SyncVolume()
        => _audioPlayer?.SetVolume((float)(VolumeLevel / 100.0), IsMuted);

    partial void OnSelectedSpeedIndexChanged(int value)
        => _playbackSpeed = value >= 0 && value < SpeedValues.Length
            ? SpeedValues[value] : 1.0;

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    // ── 공개 진입점: 키보드 단축키 ────────────────────────────────────

    /// <summary>현재 위치에서 <paramref name="deltaSeconds"/>만큼 상대 시크합니다.</summary>
    public async Task SeekRelativeAsync(double deltaSeconds)
    {
        if (!IsFileOpen || DurationSeconds == 0) return;
        var newPos = Math.Clamp(PositionSeconds + deltaSeconds, 0, DurationSeconds);
        _isUpdatingPositionFromPlayback = true;
        PositionSeconds = newPos;
        _isUpdatingPositionFromPlayback = false;
        await UserSeekAsync(TimeSpan.FromSeconds(newPos));
    }

    // ── Seek slider 변경 처리 ─────────────────────────────────────────

    partial void OnPositionSecondsChanged(double value)
    {
        if (_isUpdatingPositionFromPlayback) return;

        // 150ms 디바운스: 슬라이더 드래그 중 과도한 시크 요청 방지
        _seekDebounce?.Cancel();
        _seekDebounce?.Dispose();
        _seekDebounce = new CancellationTokenSource();
        var ct = _seekDebounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, ct);
                await UserSeekAsync(TimeSpan.FromSeconds(value));
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    // ── 공개 진입점: DragDrop ─────────────────────────────────────────

    /// <summary>View 코드-비하인드의 DragDrop 핸들러에서 호출합니다.</summary>
    public Task OpenDroppedFileAsync(string filePath) => OpenFileInternalAsync(filePath);

    // ═══════════════════════════════════════════════════════════════
    // 파일 열기
    // ═══════════════════════════════════════════════════════════════

    private async Task OpenFileInternalAsync(string filePath)
    {
        StopPlaybackLoop(pauseAudio: false);
        _pausedPosition = TimeSpan.Zero;

        // ── 기존 리소스 해제 ─────────────────────────────────────────
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        if (_videoDecoder != null) { await _videoDecoder.DisposeAsync(); _videoDecoder = null; }
        if (_audioDecoder != null) { await _audioDecoder.DisposeAsync(); _audioDecoder = null; }

        // ── 새 인스턴스 취득 (Transient) ─────────────────────────────
        _videoDecoder = _services.GetRequiredService<IVideoDecoder>();

        try
        {
            StatusText = "파일 열기 중…";
            await _videoDecoder.OpenAsync(filePath);

            IsFileOpen      = true;
            DurationSeconds = _videoDecoder.Duration.TotalSeconds;

            _isUpdatingPositionFromPlayback = true;
            PositionSeconds = 0;
            _isUpdatingPositionFromPlayback = false;

            UpdateTimecode(TimeSpan.Zero);

            _mainVm.CurrentFileName    = Path.GetFileName(filePath);
            _mainVm.PlaybackStatusText = "준비";
            _mainVm.PlaybackStatusIcon = "Stop24";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "비디오 스트림 열기 실패: {File}", filePath);
            StatusText = $"열기 실패: {ex.Message}";
            IsFileOpen = false;
            return;
        }

        // ── 오디오 스트림 열기 (없어도 계속 진행) ────────────────────
        await TryOpenAudioAsync(filePath);

        UpdateStatus("준비");
    }

    private async Task TryOpenAudioAsync(string filePath)
    {
        try
        {
            var decoder = _services.GetRequiredService<IAudioDecoder>();
            await decoder.OpenAsync(filePath);

            _audioDecoder = decoder;

            var info = _audioDecoder.StreamInfo!;
            // waveOutOpen은 대부분 드라이버에서 2ch까지 지원. PcmFormatConverter가
            // 디코딩 시 스테레오로 다운믹스하므로 출력 채널도 동일하게 캡핑합니다.
            int outChannels = Math.Min(info.Channels, 2);
            _audioPlayer = new NAudioPlayer();
            _audioPlayer.Initialize(info.SampleRate, outChannels);
            SyncVolume();

            _logger.LogInformation(
                "오디오 출력 초기화: {Rate}Hz {Ch}ch → {OutCh}ch ({Layout})",
                info.SampleRate, info.Channels, outChannels, info.ChannelLayout);
        }
        catch (MediaReadException ex)
        {
            // 오디오 스트림 없음 — 비디오만 재생 (정상 케이스)
            _logger.LogInformation("오디오 스트림 없음 — 비디오만 재생합니다. ({Msg})", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "오디오 초기화 실패 — 비디오만 재생합니다.");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 사용자 시크
    // ═══════════════════════════════════════════════════════════════

    private async Task UserSeekAsync(TimeSpan position)
    {
        if (_videoDecoder == null || !_videoDecoder.IsOpen) return;

        var wasPlaying = IsPlaying;
        StopPlaybackLoop(pauseAudio: false);

        try
        {
            await Task.WhenAll(
                _videoDecoder.SeekAsync(position),
                _audioDecoder?.IsOpen == true
                    ? _audioDecoder.SeekAsync(position)
                    : Task.CompletedTask);

            _audioPlayer?.ClearBuffer();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "시크 실패: {Position}", position);
        }

        _pausedPosition = position;

        await Application.Current.Dispatcher.InvokeAsync(() => UpdateTimecode(position));

        if (wasPlaying)
            StartPlaybackLoop();
    }

    // ═══════════════════════════════════════════════════════════════
    // 재생 루프 관리
    // ═══════════════════════════════════════════════════════════════

    private void StartPlaybackLoop()
    {
        IsPlaying = true;
        _mainVm.PlaybackStatusText = "재생 중";
        _mainVm.PlaybackStatusIcon = "Play24";

        _playbackCts = new CancellationTokenSource();
        var ct = _playbackCts.Token;

        // 비디오 디코드 + 렌더 루프
        _videoLoopTask = Task.Run(() => VideoPlaybackLoopAsync(ct), ct);

        // 오디오 디코드 + 피드 루프 (오디오 스트림 있는 경우만)
        if (_audioDecoder != null && _audioPlayer != null)
        {
            _audioPlayer.Play();
            _audioLoopTask = Task.Run(() => AudioPlaybackLoopAsync(ct), ct);
        }
    }

    private void StopPlaybackLoop(bool pauseAudio)
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;

        if (pauseAudio)
            _audioPlayer?.Pause();
        else
            _audioPlayer?.Stop();
    }

    // ═══════════════════════════════════════════════════════════════
    // 비디오 재생 루프 (백그라운드 스레드)
    // ═══════════════════════════════════════════════════════════════

    private async Task VideoPlaybackLoopAsync(CancellationToken ct)
    {
        var sw       = Stopwatch.StartNew();
        var firstPts = TimeSpan.MinValue;

        try
        {
            await foreach (var frame in _videoDecoder!.DecodeAsync(ct))
            {
                if (firstPts == TimeSpan.MinValue)
                {
                    firstPts = frame.Timestamp;
                    sw.Restart();
                }

                // PTS 기반 프레임 타이밍: 재생 속도로 목표 경과시간을 나눠 빠르기 조정
                var targetMs = (frame.Timestamp - firstPts).TotalMilliseconds / _playbackSpeed;
                var delay    = TimeSpan.FromMilliseconds(targetMs) - sw.Elapsed;
                if (delay > TimeSpan.FromMilliseconds(1))
                    await Task.Delay(delay, ct);

                await Application.Current.Dispatcher.InvokeAsync(
                    () => RenderFrame(frame),
                    DispatcherPriority.Render);
            }

            // 파일 끝 — 자연 종료
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying = false;
                _pausedPosition = TimeSpan.Zero;
                UpdateStatus("재생 완료");
                _mainVm.PlaybackStatusText = "정지";
                _mainVm.PlaybackStatusIcon = "Stop24";
            });
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "비디오 재생 루프 오류");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying = false;
                StatusText = $"재생 오류: {ex.Message}";
                _mainVm.PlaybackStatusText = "오류";
                _mainVm.PlaybackStatusIcon = "Stop24";
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 오디오 재생 루프 (백그라운드 스레드)
    // ═══════════════════════════════════════════════════════════════

    private async Task AudioPlaybackLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _audioDecoder!.DecodeAsync(ct))
            {
                // 버퍼가 0.8초 이상 차면 대기 — 오버버퍼링 및 시크 지연 방지
                while (_audioPlayer != null
                       && _audioPlayer.BufferedDuration > TimeSpan.FromSeconds(0.8))
                {
                    await Task.Delay(20, ct);
                }

                _audioPlayer?.QueueSamples(frame.Samples);
            }
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "오디오 재생 루프 오류");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // WriteableBitmap 렌더링 (UI 스레드 전용)
    // ═══════════════════════════════════════════════════════════════

    private void RenderFrame(VideoFrame frame)
    {
        // 크기 변경 시에만 새 WriteableBitmap 생성
        if (VideoBitmap == null
            || VideoBitmap.PixelWidth  != frame.Width
            || VideoBitmap.PixelHeight != frame.Height)
        {
            VideoBitmap = new WriteableBitmap(
                frame.Width, frame.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);
            OnPropertyChanged(nameof(VideoBitmap));
        }

        VideoBitmap.Lock();
        try
        {
            VideoBitmap.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.Data,
                frame.Stride,
                0);
        }
        finally
        {
            VideoBitmap.Unlock();
        }

        _isUpdatingPositionFromPlayback = true;
        PositionSeconds = frame.Timestamp.TotalSeconds;
        _isUpdatingPositionFromPlayback = false;

        UpdateTimecode(frame.Timestamp);
    }

    // ═══════════════════════════════════════════════════════════════
    // 헬퍼
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTimecode(TimeSpan position)
    {
        var dur = TimeSpan.FromSeconds(DurationSeconds);
        TimecodeText = $"{FormatTs(position)} / {FormatTs(dur)}";
    }

    private void UpdateStatus(string text) => StatusText = text;

    private static string FormatTs(TimeSpan ts)
        => ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";

    // ═══════════════════════════════════════════════════════════════
    // IDisposable — DI 컨테이너가 앱 종료 시 호출
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        // 1. 재생 루프 취소 — 백그라운드 스레드가 CT 체크포인트에서 빠져나오도록 신호
        StopPlaybackLoop(pauseAudio: false);
        _seekDebounce?.Cancel();
        _seekDebounce?.Dispose();

        // 2. 루프 태스크가 완료될 때까지 최대 2초 대기 (데드락 방지: UI 스레드 아닌 경우만)
        var loops = new[] { _videoLoopTask, _audioLoopTask }
            .Where(t => t is not null)
            .Cast<Task>()
            .ToArray();

        if (loops.Length > 0)
            Task.WhenAll(loops).Wait(TimeSpan.FromSeconds(2));

        // 3. 오디오 출력 해제
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        // 4. 디코더 해제 (DisposeAsync는 내부적으로 동기 작업만 수행하므로 .GetResult() 안전)
        _videoDecoder?.DisposeAsync().GetAwaiter().GetResult();
        _videoDecoder = null;
        _audioDecoder?.DisposeAsync().GetAwaiter().GetResult();
        _audioDecoder = null;
    }
}
