using System.Windows.Controls;
using System.Windows.Media;
using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Models;
using fflux.Core.Models.Options;
using fflux.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wpf.Ui;
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
    private readonly IContentDialogService    _dialogService;
    private readonly ISettingsService         _settingsService;
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

    // 파일 오픈 시 저장되는 프레임레이트 (프레임 스텝 계산용)
    private double _frameRate;

    // 프레임 스텝의 동시 실행을 막는 세마포어 (hold-to-repeat 시 디코딩 중복 방지)
    private readonly SemaphoreSlim _stepSemaphore = new(1, 1);

    // ── 렌더링: CompositionTarget.Rendering + Interlocked 패턴 ─────
    //
    // 비디오 루프(백그라운드): PTS 타이밍 후 _pendingFrame에 원자적 교체
    // CompositionTarget.Rendering(UI 스레드): vsync마다 _pendingFrame 꺼내 렌더링
    //
    // Dispatcher.InvokeAsync(priority=Render) 대비 이점:
    //  - Render(7) < Normal(9) 우선순위 → 레이아웃·바인딩에 밀리는 지터 제거
    //  - 비디오 루프가 렌더 완료를 기다리지 않아 다음 프레임 준비를 즉시 시작
    //  - vsync에 정확히 동기화 → 찢김 없는 매끄러운 출력

    private VideoFrame? _pendingFrame;       // 백그라운드 ↔ UI 공유 (Interlocked 전용)
    private bool        _renderingSubscribed; // CompositionTarget.Rendering 구독 여부

    // ── 재생 루프 태스크 ─────────────────────────────────────────────
    // 이전 재생이 완전히 종료된 것을 확인한 후 디코더를 교체하기 위해 참조를 보관합니다.

    // ── 자막 ─────────────────────────────────────────────────────────

    private IReadOnlyList<SubtitleEntry> _subtitleEntries = [];

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
    [NotifyCanExecuteChangedFor(nameof(StepFrameForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepFrameBackwardCommand))]
    [NotifyPropertyChangedFor(nameof(CanSeek))]
    private bool _isFileOpen;

    /// <summary>UDP / RTP / RTSP 등 라이브 스트림 재생 중이면 true. 시크·프레임스텝 비활성화.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepFrameForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepFrameBackwardCommand))]
    [NotifyPropertyChangedFor(nameof(CanSeek))]
    private bool _isLiveStream;

    /// <summary>파일이 열려 있고 라이브 스트림이 아닐 때만 시크 가능.</summary>
    public bool CanSeek => IsFileOpen && !IsLiveStream;

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

    /// <summary>풀스크린 상태. 코드-비하인드가 PropertyChanged를 구독하여 창 전환을 수행합니다.</summary>
    [ObservableProperty]
    private bool _isFullscreen;

    // ── 자막 프로퍼티 ────────────────────────────────────────────────

    /// <summary>현재 재생 위치에서 표시할 자막 텍스트. 빈 문자열이면 자막 없음.</summary>
    [ObservableProperty] private string _subtitleText = string.Empty;

    /// <summary>자막 표시 토글 상태.</summary>
    [ObservableProperty] private bool _isSubtitleVisible = true;

    /// <summary>자막 폰트 크기 (기본값: 22pt).</summary>
    [ObservableProperty] private double _subtitleFontSize = 22.0;

    /// <summary>자막 텍스트 색상 (기본값: White).</summary>
    [ObservableProperty] private Brush _subtitleColor = Brushes.White;

    public SymbolRegular VolumeSymbol =>
        IsMuted || VolumeLevel == 0  ? SymbolRegular.SpeakerMute24 :
        VolumeLevel < 50             ? SymbolRegular.Speaker120 :
                                       SymbolRegular.Speaker224;

    // ── 생성자 ──────────────────────────────────────────────────────

    public PlayerViewModel(
        IServiceProvider          services,
        MainWindowViewModel       mainVm,
        IContentDialogService     dialogService,
        ISettingsService          settingsService,
        ILogger<PlayerViewModel>  logger)
    {
        _services        = services;
        _mainVm          = mainVm;
        _dialogService   = dialogService;
        _settingsService = settingsService;
        _logger          = logger;
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

        await OpenSourceInternalAsync(dlg.FileName, isLive: false);
    }

    /// <summary>UDP / RTP / RTSP URL을 입력받아 라이브 스트리밍을 시작합니다.</summary>
    [RelayCommand]
    private async Task OpenStreamAsync()
    {
        // ── URL 입력 다이얼로그 ──────────────────────────────────────
        var urlBox = new System.Windows.Controls.TextBox
        {
            MinWidth  = 400,
            FontSize  = 13,
            Margin    = new Thickness(0, 10, 0, 0),
            Text      = "rtsp://",
        };

        var hint = new System.Windows.Controls.TextBlock
        {
            Text     = "예)  rtsp://192.168.0.1:554/stream\n" +
                       "     udp://@239.0.0.1:1234\n" +
                       "     rtp://224.0.0.1:5004",
            FontSize = 11,
            Margin   = new Thickness(0, 6, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
        };

        var panel = new StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text     = "스트리밍 주소를 입력하세요:",
            FontSize = 13,
        });
        panel.Children.Add(urlBox);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            Title              = "스트리밍 열기",
            Content            = panel,
            PrimaryButtonText  = "열기",
            CloseButtonText    = "취소",
            DefaultButton      = ContentDialogButton.Primary,
        };

        // 다이얼로그가 열리면 TextBox에 포커스
        dialog.Loaded += (_, _) =>
        {
            urlBox.Focus();
            urlBox.SelectAll();
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        var url = urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        await OpenSourceInternalAsync(url, isLive: IsStreamUrl(url));
    }

    /// <summary>URL 스킴으로 라이브 스트림 여부를 판단합니다.</summary>
    private static bool IsStreamUrl(string url)
        => url.StartsWith("rtsp://",  StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("rtp://",   StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("udp://",   StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("srt://",   StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("rtmp://",  StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);

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

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    // ── 자막 커맨드 ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadSubtitleAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "자막 파일 열기",
            Filter = "자막 파일|*.srt;*.vtt|SubRip|*.srt|WebVTT|*.vtt|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        await LoadSubtitleFromPathAsync(dlg.FileName);
    }

    [RelayCommand]
    private void ToggleSubtitle()
    {
        IsSubtitleVisible = !IsSubtitleVisible;
    }

    partial void OnIsSubtitleVisibleChanged(bool value)
        => UpdateSubtitle(TimeSpan.FromSeconds(PositionSeconds));

    /// <summary>드래그앤드롭 및 커맨드에서 공통으로 사용하는 자막 로드 진입점.</summary>
    public async Task LoadSubtitleFromPathAsync(string filePath)
    {
        var ext    = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');
        var parser = _services.GetKeyedService<ISubtitleParser>(ext);

        if (parser == null)
        {
            _logger.LogWarning("지원하지 않는 자막 형식: {Ext}", ext);
            StatusText = $"지원하지 않는 자막 형식: .{ext}";
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            _subtitleEntries = parser.Parse(content);
            IsSubtitleVisible = true;
            UpdateSubtitle(TimeSpan.FromSeconds(PositionSeconds));
            StatusText = $"자막: {Path.GetFileName(filePath)} ({_subtitleEntries.Count}개)";
            _logger.LogInformation("자막 로드: {File} — {Count}개", Path.GetFileName(filePath), _subtitleEntries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "자막 로드 실패: {File}", filePath);
            StatusText = $"자막 로드 실패: {ex.Message}";
        }
    }

    // 재생 위치에 해당하는 자막 텍스트를 업데이트합니다.
    private void UpdateSubtitle(TimeSpan position)
    {
        if (!IsSubtitleVisible || _subtitleEntries.Count == 0)
        {
            SubtitleText = string.Empty;
            return;
        }
        SubtitleText = FindSubtitleEntry(position)?.Text ?? string.Empty;
    }

    // 정렬된 목록에서 Start ≤ position < End 인 항목을 이진탐색으로 찾습니다.
    private SubtitleEntry? FindSubtitleEntry(TimeSpan position)
    {
        int lo = 0, hi = _subtitleEntries.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var e = _subtitleEntries[mid];
            if (e.End <= position)
                lo = mid + 1;
            else if (e.Start > position)
                hi = mid - 1;
            else
                return e;
        }
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanStep), AllowConcurrentExecutions = true)]
    private async Task StepFrameForwardAsync()
        => await StepFrameAsync(forward: true);

    [RelayCommand(CanExecute = nameof(CanStep), AllowConcurrentExecutions = true)]
    private async Task StepFrameBackwardAsync()
        => await StepFrameAsync(forward: false);

    private bool CanStep() => IsFileOpen && !IsLiveStream;

    /// <summary>
    /// 재생 중이면 일시정지 후 1프레임 이동합니다.
    /// 세마포어로 직렬화하여 빠른 연속 입력(hold-to-repeat)이 겹치지 않도록 합니다.
    /// </summary>
    private async Task StepFrameAsync(bool forward)
    {
        if (_videoDecoder == null || !_videoDecoder.IsOpen) return;

        // 이미 스텝 실행 중이면 건너뜀 (hold-to-repeat 시 디코딩 중복 방지)
        if (!await _stepSemaphore.WaitAsync(0)) return;
        try
        {
            // 재생 중이면 일시정지
            if (IsPlaying)
            {
                _pausedPosition = TimeSpan.FromSeconds(PositionSeconds);
                StopPlaybackLoop(pauseAudio: true);
                IsPlaying = false;
                UpdateStatus("일시 정지");

                // 백그라운드 루프가 실제로 멈출 때까지 대기 (최대 500ms)
                var loops = new[] { _videoLoopTask, _audioLoopTask }
                    .Where(t => t is { IsCompleted: false })
                    .Cast<Task>()
                    .ToArray();
                if (loops.Length > 0)
                {
                    try { await Task.WhenAll(loops).WaitAsync(TimeSpan.FromMilliseconds(500)); }
                    catch { /* OperationCanceledException 또는 타임아웃 — 무시 */ }
                }
            }

            VideoFrame? frame;
            if (forward)
            {
                // 앞으로: 현재 위치 이후 첫 프레임을 반환
                var current = TimeSpan.FromSeconds(PositionSeconds);
                frame = await _videoDecoder.DecodeNextFrameAfterAsync(current);
            }
            else
            {
                // 뒤로: 1프레임 이전 위치로 seek
                var step   = FrameStepSeconds();
                var rawPos = PositionSeconds - step;
                var target = TimeSpan.FromSeconds(Math.Clamp(rawPos, 0, DurationSeconds));
                frame = await _videoDecoder.SeekAndDecodeAtAsync(target);
            }

            if (frame is not null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => { RenderFrame(frame); frame.Dispose(); });
                _pausedPosition = frame.Timestamp;
            }
        }
        finally
        {
            _stepSemaphore.Release();
        }
    }

    private double FrameStepSeconds()
        => _frameRate > 1.0 ? 1.0 / _frameRate : 1.0 / 30.0;

    // ── 공개 진입점: 키보드 단축키 ────────────────────────────────────

    /// <summary>현재 위치에서 <paramref name="deltaSeconds"/>만큼 상대 시크합니다.</summary>
    public async Task SeekRelativeAsync(double deltaSeconds)
    {
        if (!IsFileOpen || IsLiveStream || DurationSeconds == 0) return;
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
    public Task OpenDroppedFileAsync(string filePath) => OpenSourceInternalAsync(filePath, isLive: false);

    // ═══════════════════════════════════════════════════════════════
    // 파일 열기
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 파일 경로 또는 스트리밍 URL을 열고 재생을 준비합니다.
    /// </summary>
    /// <param name="source">로컬 파일 경로 또는 rtsp:// · rtp:// · udp:// 등의 URL</param>
    /// <param name="isLive">라이브 스트림이면 true (시크·프레임스텝 비활성화)</param>
    private async Task OpenSourceInternalAsync(string source, bool isLive)
    {
        StopPlaybackLoop(pauseAudio: false);
        _pausedPosition = TimeSpan.Zero;

        // ── 이전 재생 루프가 완전히 종료될 때까지 대기 ──────────────
        // StopPlaybackLoop는 CancellationToken을 취소하는 것에 불과합니다.
        // 취소 신호를 받은 백그라운드 루프가 실제로 종료될 때까지 대기하지 않으면
        // 루프가 여전히 실행 중인 상태에서 디코더를 Dispose하여 크래시가 발생합니다.
        var prevLoops = new[] { _videoLoopTask, _audioLoopTask }
            .Where(t => t is { IsCompleted: false })
            .Cast<Task>()
            .ToArray();
        if (prevLoops.Length > 0)
        {
            try
            {
                await Task.WhenAll(prevLoops).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 타임아웃 또는 루프 내부 예외 — 계속 진행합니다.
            }
        }
        _videoLoopTask = null;
        _audioLoopTask = null;

        // 자막 초기화 (라이브 스트림에서는 자막 미지원)
        _subtitleEntries = [];
        SubtitleText     = string.Empty;

        // ── 기존 리소스 해제 ─────────────────────────────────────────
        _audioPlayer?.Dispose();
        _audioPlayer = null;

        if (_videoDecoder != null) { await _videoDecoder.DisposeAsync(); _videoDecoder = null; }
        if (_audioDecoder != null) { await _audioDecoder.DisposeAsync(); _audioDecoder = null; }

        // ── 새 인스턴스 취득 (Transient) ─────────────────────────────
        _videoDecoder = _services.GetRequiredService<IVideoDecoder>();

        try
        {
            StatusText = isLive ? "스트림 연결 중…" : "파일 열기 중…";
            var openOpts = BuildVideoOpenOptions(isLive);
            await _videoDecoder.OpenAsync(source, options: openOpts);

            IsLiveStream    = isLive;
            IsFileOpen      = true;
            // 라이브 스트림은 Duration을 알 수 없으므로 0으로 둠
            DurationSeconds = isLive ? 0 : _videoDecoder.Duration.TotalSeconds;
            _frameRate      = _videoDecoder.StreamInfo?.FrameRate ?? 0;

            _isUpdatingPositionFromPlayback = true;
            PositionSeconds = 0;
            _isUpdatingPositionFromPlayback = false;

            UpdateTimecode(TimeSpan.Zero);

            // 상태바 표시: 라이브는 URL 그대로, 파일은 파일명만
            _mainVm.CurrentFileName    = isLive ? source : Path.GetFileName(source);
            _mainVm.PlaybackStatusText = "준비";
            _mainVm.PlaybackStatusIcon = "Stop24";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "소스 열기 실패: {Source}", source);
            StatusText   = $"열기 실패: {ex.Message}";
            IsFileOpen   = false;
            IsLiveStream = false;
            return;
        }

        // ── 오디오 스트림 열기 (없어도 계속 진행) ────────────────────
        await TryOpenAudioAsync(source);

        // ── 동일 이름 자막 파일 자동 로드 (파일 전용, 라이브 스트림 제외) ──
        if (!isLive)
            await TryAutoLoadSubtitleAsync(source);

        UpdateStatus(isLive ? "● LIVE 연결됨" : "재생 중");

        // ── 자동 재생 ────────────────────────────────────────────────────
        StartPlaybackLoop();
    }

    private async Task TryAutoLoadSubtitleAsync(string filePath)
    {
        var dir  = Path.GetDirectoryName(filePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(filePath);

        // .srt 우선, 없으면 .vtt 탐색
        string? subtitlePath = null;
        foreach (var ext in new[] { ".srt", ".vtt" })
        {
            var candidate = Path.Combine(dir, stem + ext);
            if (File.Exists(candidate)) { subtitlePath = candidate; break; }
        }

        if (subtitlePath == null) return;

        await LoadSubtitleFromPathAsync(subtitlePath);
        _logger.LogInformation("자막 자동 로드: {File}", Path.GetFileName(subtitlePath));
    }

    /// <summary>현재 앱 설정으로부터 <see cref="VideoOpenOptions"/>를 생성합니다.</summary>
    /// <param name="isLive">라이브 스트림이면 스트리밍 옵션을 채웁니다.</param>
    private VideoOpenOptions BuildVideoOpenOptions(bool isLive)
    {
        var cur = _settingsService.Current;
        var sd  = cur.Decoder;
        var st  = cur.Streaming;

        return new VideoOpenOptions
        {
            // ── 공통 디코더 옵션 ──────────────────────────────────────
            HwAccel         = sd.HwAccel,
            FileThreadCount = sd.FileThreadCount,
            SkipLoopFilter  = sd.SkipLoopFilter,
            SkipFrame       = sd.SkipFrame,

            // ── 스트리밍 옵션 (라이브일 때만 의미 있음) ───────────────
            RtspTransport            = st.RtspTransport,
            TimeoutSeconds           = st.TimeoutSeconds,
            ProbeSizeKb              = st.ProbeSizeKb,
            AnalyzeDurationSeconds   = st.AnalyzeDurationSeconds,
            NoBuffer                 = isLive && st.NoBuffer,
            MaxDelayMs               = st.MaxDelayMs,
            LiveThreadCount          = st.LiveThreadCount,
            RecvBufferSizeKb         = st.RecvBufferSizeKb,
            ReorderQueueSize         = st.ReorderQueueSize,
            Reconnect                = isLive && st.Reconnect,
            ReconnectDelayMaxSeconds = st.ReconnectDelayMaxSeconds,
        };
    }

    private async Task TryOpenAudioAsync(string filePath)
    {
        // RTSP / RTP / UDP 등 라이브 스트림은 비디오와 동일한 단일 연결에서
        // 오디오를 수신해야 합니다. 별도 연결을 시도하면 서버가 두 번째 접속을
        // 거부하거나 avformat_find_stream_info에서 ENOMEM 오류가 발생합니다.
        if (IsLiveStream) return;

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

        // vsync 동기화 렌더링 구독 (UI 스레드에서만 호출됨)
        if (!_renderingSubscribed)
        {
            _renderingSubscribed = true;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }

        // 비디오 디코드+PTS타이밍 루프 (렌더링은 OnCompositionTargetRendering에서 담당)
        _videoLoopTask = Task.Run(() => VideoPlaybackLoopAsync(ct), ct);

        // 오디오 디코드 + 피드 루프 (오디오 스트림 있는 경우만)
        if (_audioDecoder != null && _audioPlayer != null)
        {
            _audioPlayer.Play();
            _audioLoopTask = Task.Run(() => AudioPlaybackLoopAsync(ct), ct);
        }
    }

    /// <summary>
    /// WPF 디스플레이 vsync 이벤트 핸들러 — UI 스레드에서 매 vsync마다 실행됩니다.
    /// 비디오 루프가 PTS 타이밍으로 준비한 프레임을 렌더링합니다.
    /// </summary>
    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        var frame = Interlocked.Exchange(ref _pendingFrame, null);
        if (frame is null) return;

        RenderFrame(frame);
        frame.Dispose(); // ArrayPool 버퍼 반환
    }

    private void StopPlaybackLoop(bool pauseAudio)
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;

        // vsync 렌더링 이벤트 해제 + 미처리 프레임 반환 (UI 스레드에서 호출 보장)
        if (_renderingSubscribed)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _renderingSubscribed = false;
        }
        Interlocked.Exchange(ref _pendingFrame, null)?.Dispose();

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
                // ── PTS 기반 타이밍 ────────────────────────────────────────────
                //
                // [버그 방어] firstPts를 TimeSpan.Zero 프레임에 앵커링하면 안 됩니다.
                //
                // 일부 컨테이너(AVI, 불량 MP4 등)는 초반 N개 프레임의 pts가
                // AV_NOPTS_VALUE이거나 0입니다. 이 프레임들의 Timestamp = Zero.
                // Zero로 앵커링하면 이후 진짜 PTS(예: 2.5s)를 가진 프레임에서
                // delayMs = 2500ms - 경과(50ms) = 2450ms → 긴 멈춤이 발생합니다.
                //
                // 해결: Zero-PTS 프레임은 frameRate 기반으로 속도를 제한하고,
                //       firstPts는 처음으로 양수 PTS를 가진 프레임에서 앵커링합니다.

                if (frame.Timestamp > TimeSpan.Zero)
                {
                    if (firstPts == TimeSpan.MinValue)
                    {
                        firstPts = frame.Timestamp;
                        sw.Restart();
                    }

                    var targetMs = (frame.Timestamp - firstPts).TotalMilliseconds / _playbackSpeed;
                    var delayMs  = targetMs - sw.Elapsed.TotalMilliseconds;

                    if (delayMs > 2.0)
                        await Task.Delay((int)delayMs, ct);
                }
                else
                {
                    // PTS 없는 프레임: 선언된 frameRate로 속도 제한 (최소 1ms)
                    // best_effort_timestamp 폴백 후에도 Zero면 컨테이너에 PTS 정보 없음
                    var frameDurMs = _frameRate > 1.0
                        ? (int)(1000.0 / _frameRate / _playbackSpeed)
                        : 33; // 기본 ~30fps
                    if (frameDurMs > 1)
                        await Task.Delay(frameDurMs, ct);
                }

                // ── 비블로킹 렌더 전달 ────────────────────────────────────────
                // UI 스레드를 기다리지 않고 _pendingFrame에 원자적으로 교체합니다.
                // CompositionTarget.Rendering이 다음 vsync에 픽업하여 렌더링합니다.
                //
                // 만약 이전 프레임이 아직 렌더되지 않은 상태(PTS 타이밍 오차 등)라면
                // 이전 프레임을 즉시 풀에 반환하고 최신 프레임으로 교체합니다.
                Interlocked.Exchange(ref _pendingFrame, frame)?.Dispose();
            }

            // 스트림/파일 자연 종료 — UI 스레드에서 정리
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 미처리 프레임 반환 및 렌더링 이벤트 해제
                if (_renderingSubscribed)
                {
                    CompositionTarget.Rendering -= OnCompositionTargetRendering;
                    _renderingSubscribed = false;
                }
                Interlocked.Exchange(ref _pendingFrame, null)?.Dispose();

                var wasLive     = IsLiveStream;
                IsPlaying       = false;
                IsLiveStream    = false;
                _pausedPosition = TimeSpan.Zero;
                UpdateStatus(wasLive ? "스트림 연결 끊김" : "재생 완료");
                _mainVm.PlaybackStatusText = "정지";
                _mainVm.PlaybackStatusIcon = "Stop24";
            });
        }
        catch (OperationCanceledException)
        {
            // 정상 취소 — 미처리 프레임만 반환 (StopPlaybackLoop에서 이미 처리됨)
            Interlocked.Exchange(ref _pendingFrame, null)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "비디오 재생 루프 오류");
            Interlocked.Exchange(ref _pendingFrame, null)?.Dispose();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying  = false;
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
        bool isLive = IsLiveStream;

        // 라이브: 100ms 이하로 유지 → A/V 동기화 지연 최소화
        // 파일:  800ms 선버퍼 → 시크 후 빠른 오디오 복구
        var maxBuffer = isLive
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromMilliseconds(800);

        try
        {
            await foreach (var frame in _audioDecoder!.DecodeAsync(ct))
            {
                while (_audioPlayer != null
                       && _audioPlayer.BufferedDuration > maxBuffer)
                {
                    // 라이브: 빠른 폴링으로 지연 최소화, 파일: 여유롭게 대기
                    await Task.Delay(isLive ? 5 : 20, ct);
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

        // 라이브 스트림은 PTS 기반 위치 표시가 무의미하므로 슬라이더를 갱신하지 않습니다.
        if (!IsLiveStream)
        {
            _isUpdatingPositionFromPlayback = true;
            PositionSeconds = frame.Timestamp.TotalSeconds;
            _isUpdatingPositionFromPlayback = false;
        }

        UpdateTimecode(frame.Timestamp);
    }

    // ═══════════════════════════════════════════════════════════════
    // 헬퍼
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTimecode(TimeSpan position)
    {
        var dur = TimeSpan.FromSeconds(DurationSeconds);
        TimecodeText = $"{FormatTs(position)} / {FormatTs(dur)}";
        UpdateSubtitle(position);
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

        _stepSemaphore.Dispose();
    }
}
