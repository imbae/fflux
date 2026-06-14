using System.Collections.ObjectModel;
using System.Windows;
using fflux.UI.Modules.Player;
using fflux.UI.Modules.ScreenRecorder.Core.Models;
using fflux.UI.Modules.ScreenRecorder.Core.Services;
using fflux.UI.Modules.ScreenRecorder.Drawing;
using fflux.UI.Modules.ScreenRecorder.Drawing.Views;
using fflux.UI.Modules.ScreenRecorder.Views;
using fflux.UI.Shared.Services;
using Wpf.Ui;

namespace fflux.UI.Modules.ScreenRecorder;

public sealed partial class ScreenRecorderViewModel : ObservableObject, IDisposable
{
    // 핫키 ID 상수
    private const int HK_START_STOP = 9001;
    private const int HK_PAUSE      = 9002;
    private const int HK_SNAPSHOT   = 9003;

    private readonly IRecordingSessionService  _session;
    private readonly IScreenCaptureService     _captureService;
    private readonly IGlobalHotkeyService      _hotkeys;
    private readonly INavigationService        _navigation;
    private readonly IDialogService            _dialog;
    private readonly PlayerViewModel           _playerVm;
    private readonly DrawingOverlayViewModel   _drawingVm;
    private readonly ILogger<ScreenRecorderViewModel> _logger;

    private RecorderOverlayWindow?  _overlay;
    private DrawingOverlayWindow?   _drawingOverlay;
    private bool _disposed;

    // ── 모니터 목록 ─────────────────────────────────────────────

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];

    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    // ── 캡처 대상 ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullScreenMode))]
    [NotifyPropertyChangedFor(nameof(IsRegionMode))]
    [NotifyPropertyChangedFor(nameof(IsWindowMode))]
    private CaptureMode _captureMode = CaptureMode.FullScreen;

    public bool IsFullScreenMode
    {
        get => CaptureMode == CaptureMode.FullScreen;
        set { if (value) CaptureMode = CaptureMode.FullScreen; }
    }
    public bool IsRegionMode
    {
        get => CaptureMode == CaptureMode.Region;
        set { if (value) CaptureMode = CaptureMode.Region; }
    }
    public bool IsWindowMode
    {
        get => CaptureMode == CaptureMode.Window;
        set { if (value) CaptureMode = CaptureMode.Window; }
    }

    // ── 오디오 ──────────────────────────────────────────────────

    [ObservableProperty] private bool _recordSystemAudio = true;
    [ObservableProperty] private bool _recordMicrophone  = false;

    // ── FPS ─────────────────────────────────────────────────────

    [ObservableProperty] private int _targetFps = 30;

    // ── 출력 경로 ───────────────────────────────────────────────

    [ObservableProperty] private string _outputDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    // ── 녹화 상태 ───────────────────────────────────────────────

    [ObservableProperty] private bool   _isCapturing = false;
    [ObservableProperty] private string _statusText  = "대기 중";

    // ── 생성자 ──────────────────────────────────────────────────

    public ScreenRecorderViewModel(
        IRecordingSessionService          session,
        IScreenCaptureService             captureService,
        IGlobalHotkeyService              hotkeys,
        INavigationService                navigation,
        IDialogService                    dialog,
        PlayerViewModel                   playerVm,
        DrawingOverlayViewModel           drawingVm,
        ILogger<ScreenRecorderViewModel>  logger)
    {
        _session        = session;
        _captureService = captureService;
        _hotkeys        = hotkeys;
        _navigation     = navigation;
        _dialog         = dialog;
        _playerVm       = playerVm;
        _drawingVm      = drawingVm;
        _logger         = logger;

        _session.StateChanged   += OnSessionStateChanged;
        _session.ElapsedUpdated += OnElapsedUpdated;

        LoadMonitors();
        RegisterHotkeys();
    }

    // ── 커맨드 ──────────────────────────────────────────────────

    [RelayCommand]
    private void SetFps(string fpsStr)
    {
        if (int.TryParse(fpsStr, out var fps)) TargetFps = fps;
    }

    [RelayCommand]
    private void LoadMonitors()
    {
        try
        {
            Monitors.Clear();
            foreach (var m in _captureService.GetAvailableMonitors())
                Monitors.Add(m);
            SelectedMonitor = Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "모니터 목록 조회 실패");
        }
    }

    [RelayCommand]
    private void SelectOutputDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "녹화 파일 저장 폴더 선택",
            UseDescriptionForTitle = true,
            SelectedPath           = OutputDirectory,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputDirectory = dialog.SelectedPath;
    }

    // ToggleCapture는 void — RelayCommand 실행 중 버튼이 비활성화되지 않음
    [RelayCommand]
    private void ToggleCapture()
    {
        if (_session.State == RecordingState.Idle)
            _ = StartAsync();
        else
            _ = StopAsync();
    }

    // ── 녹화 시작 / 정지 ────────────────────────────────────────

    private async Task StartAsync()
    {
        try
        {
            await _session.StartAsync(BuildSettings());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "녹화 시작 실패");
            await _dialog.ShowErrorAsync("녹화 오류", "녹화를 시작할 수 없습니다.", ex);
        }
    }

    private async Task StopAsync()
    {
        var outputPath = await _session.StopAsync();

        // 7. 녹화 후 즉시 재생 연계
        if (outputPath is not null && File.Exists(outputPath))
            await PromptOpenInPlayerAsync(outputPath);
    }

    private async Task PromptOpenInPlayerAsync(string outputPath)
    {
        bool open = await _dialog.ShowConfirmAsync(
            "녹화 완료",
            $"저장 완료: {Path.GetFileName(outputPath)}\n\nfflux 플레이어에서 바로 열까요?");

        if (!open) return;

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            _navigation.Navigate(typeof(fflux.UI.Modules.Player.PlayerPage));
            await Task.Delay(200); // 페이지 전환 대기
            await _playerVm.OpenDroppedFileAsync(outputPath);
        });
    }

    // ── 세션 이벤트 핸들러 ───────────────────────────────────────

    private void OnSessionStateChanged(object? sender, RecordingState state)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (state)
            {
                case RecordingState.Recording:
                    IsCapturing = true;
                    StatusText  = "녹화 중…";
                    ShowOverlay();
                    break;

                case RecordingState.Paused:
                    StatusText = "일시정지";
                    break;

                case RecordingState.Stopping:
                    StatusText = "종료 중…";
                    break;

                case RecordingState.Idle:
                    IsCapturing = false;
                    StatusText  = "대기 중";
                    CloseOverlay();
                    break;
            }
        });
    }

    private void OnElapsedUpdated(object? sender, TimeSpan elapsed)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
            StatusText = IsCapturing
                ? $"녹화 중… {elapsed:hh\\:mm\\:ss}"
                : StatusText);
    }

    // ── 플로팅 컨트롤바 ─────────────────────────────────────────

    private void ShowOverlay()
    {
        if (_overlay is not null) return;
        _overlay = new RecorderOverlayWindow(_session, this);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }

    private void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
    }

    // ── 그리기 오버레이 ──────────────────────────────────────────

    [ObservableProperty] private bool _isDrawingActive = false;

    [RelayCommand]
    private void ToggleDraw()
    {
        if (_drawingOverlay is null)
            ShowDrawingOverlay();
        else
            HideDrawingOverlay();
    }

    private void ShowDrawingOverlay()
    {
        if (_drawingOverlay is not null) return;
        _drawingOverlay = new DrawingOverlayWindow(_drawingVm);
        _drawingOverlay.Closed += (_, _) =>
        {
            _drawingOverlay = null;
            IsDrawingActive = false;
        };
        _drawingOverlay.Show();
        IsDrawingActive = true;
    }

    private void HideDrawingOverlay()
    {
        _drawingOverlay?.Close();
        _drawingOverlay = null;
        IsDrawingActive = false;
    }

    // ── 글로벌 핫키 ─────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys.Initialize();
            // F9: 시작/정지
            _hotkeys.Register(HK_START_STOP, 0, 0x78,
                () => Application.Current.Dispatcher.InvokeAsync(ToggleCapture));
            // F10: 일시정지/재개
            _hotkeys.Register(HK_PAUSE, 0, 0x79,
                () => Application.Current.Dispatcher.InvokeAsync(TogglePauseAsync));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "글로벌 핫키 등록 실패 (무시)");
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_session.State == RecordingState.Paused)
            await _session.ResumeAsync();
        else if (_session.State == RecordingState.Recording)
            await _session.PauseAsync();
    }

    // ── 설정 빌드 ───────────────────────────────────────────────

    public RecordingSettings BuildSettings() => new(
        new CaptureTarget(CaptureMode, SelectedMonitor?.Index ?? 0),
        CaptureRegion: null,
        WindowHandle:  null,
        TargetFps,
        OutputDirectory,
        RecordSystemAudio,
        RecordMicrophone);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.StateChanged   -= OnSessionStateChanged;
        _session.ElapsedUpdated -= OnElapsedUpdated;
        _hotkeys.UnregisterAll();
        CloseOverlay();
        HideDrawingOverlay();
    }
}
