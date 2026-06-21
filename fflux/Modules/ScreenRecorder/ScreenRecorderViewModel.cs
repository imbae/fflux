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
    private DrawingToolbarWindow?   _drawingToolbar;
    private RegionIndicatorWindow?  _regionIndicator;
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
    [NotifyPropertyChangedFor(nameof(IsFullScreenOrRegionMode))]
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

    /// <summary>모니터 선택 카드를 활성화할 조건 (FullScreen + Region 모드).</summary>
    public bool IsFullScreenOrRegionMode => CaptureMode != CaptureMode.Window;

    // ── 영역 선택 ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegionSummaryText))]
    private Rect? _selectedRegion;

    public string RegionSummaryText =>
        SelectedRegion.HasValue
            ? $"({(int)SelectedRegion.Value.X}, {(int)SelectedRegion.Value.Y})  " +
              $"{(int)SelectedRegion.Value.Width} × {(int)SelectedRegion.Value.Height} px"
            : "선택된 영역 없음";

    // ── 윈도우 선택 ──────────────────────────────────────────────

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = [];

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

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

    // 모니터가 바뀌면 선택 영역 초기화 (영역은 특정 모니터 기준 좌표)
    partial void OnSelectedMonitorChanged(MonitorInfo? value) => SelectedRegion = null;

    // Window 모드 진입 시 창 목록 자동 로드 + 인디케이터 갱신
    partial void OnCaptureModeChanged(CaptureMode value)
    {
        if (value == CaptureMode.Window && AvailableWindows.Count == 0)
            LoadWindows();
        UpdateRegionIndicator();
    }

    // 영역·창 변경 시 인디케이터 갱신
    partial void OnSelectedRegionChanged(Rect? value)   => UpdateRegionIndicator();
    partial void OnSelectedWindowChanged(WindowInfo? value) => UpdateRegionIndicator();

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
    private async Task SelectRegionAsync()
    {
        var monitor = SelectedMonitor
            ?? Monitors.FirstOrDefault(m => m.IsPrimary)
            ?? Monitors.FirstOrDefault();
        if (monitor is null) return;

        var picker = new Views.RegionPickerWindow(monitor);
        picker.Show();
        SelectedRegion = await picker.PickAsync();
    }

    [RelayCommand]
    private void LoadWindows()
    {
        try
        {
            AvailableWindows.Clear();
            foreach (var w in Core.Services.WindowEnumerator.GetVisibleWindows())
                AvailableWindows.Add(w);
            SelectedWindow = AvailableWindows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "윈도우 목록 조회 실패");
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
                    _regionIndicator?.SetRecordingMode();
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
                    HideDrawingOverlay();
                    _regionIndicator?.SetPreviewMode();
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

    // ── 영역 인디케이터 ──────────────────────────────────────────

    private Rect? GetIndicatorPhysicalRect() => CaptureMode switch
    {
        // Region 모드: SelectedRegion은 모니터 원점 기준 → 스크린 절대좌표로 변환
        CaptureMode.Region when SelectedRegion.HasValue && SelectedMonitor is not null =>
            new Rect(
                SelectedRegion.Value.X + SelectedMonitor.Left,
                SelectedRegion.Value.Y + SelectedMonitor.Top,
                SelectedRegion.Value.Width,
                SelectedRegion.Value.Height),
        // Window 모드: Bounds는 이미 스크린 절대좌표
        CaptureMode.Window when SelectedWindow is not null =>
            SelectedWindow.Bounds,
        _ => null
    };

    // ── 페이지 생명주기 ─────────────────────────────────────────────
    // ScreenRecorderPage.Loaded / Unloaded 이벤트에서 호출됩니다.

    public void OnPageNavigatedTo()   => UpdateRegionIndicator();
    public void OnPageNavigatedAway()
    {
        // 녹화 중에는 인디케이터·그리기 오버레이 모두 유지
        if (IsCapturing) return;
        CloseRegionIndicator();
        HideDrawingOverlay();
    }

    private void UpdateRegionIndicator()
    {
        var rect = GetIndicatorPhysicalRect();

        if (rect is null)
        {
            CloseRegionIndicator();
            return;
        }

        if (_regionIndicator is null)
        {
            _regionIndicator = new Views.RegionIndicatorWindow(rect.Value)
            {
                // 메인 창이 닫히면 오버레이도 자동으로 닫히도록 Owner를 지정합니다.
                Owner = Application.Current.MainWindow
            };
            _regionIndicator.RegionChanged += OnIndicatorRegionChanged;
            _regionIndicator.Closed        += (_, _) => _regionIndicator = null;
            _regionIndicator.Show();
        }
        else
        {
            // 인디케이터 드래그가 SelectedRegion 변경을 유발해 다시 UpdateRegionIndicator()가
            // 호출되는 피드백 루프를 방지 — 이미 같은 위치면 UpdateBounds 생략
            if (!RectsApproxEqual(_regionIndicator.PhysRect, rect.Value))
                _regionIndicator.UpdateBounds(rect.Value);
        }

        if (IsCapturing)
            _regionIndicator.SetRecordingMode();
        else
            _regionIndicator.SetPreviewMode();
    }

    private void OnIndicatorRegionChanged(object? sender, Rect physRect)
    {
        // 드래그/리사이즈 확정 → SelectedRegion을 모니터 원점 기준 좌표로 환산
        if (CaptureMode == CaptureMode.Region && SelectedMonitor is not null)
        {
            SelectedRegion = new Rect(
                physRect.X - SelectedMonitor.Left,
                physRect.Y - SelectedMonitor.Top,
                physRect.Width,
                physRect.Height);
        }
    }

    private static bool RectsApproxEqual(Rect a, Rect b)
    {
        const double eps = 0.5;
        return Math.Abs(a.X - b.X)           < eps
            && Math.Abs(a.Y - b.Y)           < eps
            && Math.Abs(a.Width  - b.Width)  < eps
            && Math.Abs(a.Height - b.Height) < eps;
    }

    private void CloseRegionIndicator()
    {
        _regionIndicator?.Close();
        _regionIndicator = null;
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
            _drawingToolbar?.Close();
            _drawingToolbar = null;
            IsDrawingActive = false;
        };
        _drawingOverlay.Show();

        _drawingToolbar = new DrawingToolbarWindow(_drawingVm);
        _drawingToolbar.Closed += (_, _) => _drawingToolbar = null;
        _drawingToolbar.Show();

        IsDrawingActive = true;
    }

    private void HideDrawingOverlay()
    {
        _drawingToolbar?.Close();
        _drawingToolbar = null;
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
        CaptureRegion: CaptureMode == CaptureMode.Region  ? SelectedRegion      : null,
        WindowHandle:  CaptureMode == CaptureMode.Window  ? SelectedWindow?.Handle : null,
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
        CloseRegionIndicator();
    }
}
