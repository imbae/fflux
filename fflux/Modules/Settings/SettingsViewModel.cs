using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.UI.Shared.Models;
using fflux.UI.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace fflux.UI.Modules.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IFFmpegInitializer _ffmpegInitializer;
    private readonly ILogger<SettingsViewModel> _logger;

    // ── FFmpeg 경로 ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(FFmpegPathValidation))]
    private string _FFmpegBinaryPath = string.Empty;

    // ── 기본 출력 폴더 ───────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(OutputFolderValidation))]
    private string _defaultOutputFolder = string.Empty;

    // ── 테마 ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    private AppTheme _selectedTheme = AppTheme.System;

    // ── 언어 ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private AppLanguage _selectedLanguage = AppLanguage.Korean;

    // ════════════════════════════════════════════════════════════════
    // 디코더 공통 옵션 (모든 소스)
    // ════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _hwAccel = "none";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _fileThreadCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _skipLoopFilter = "none";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _skipFrame = "none";

    // ════════════════════════════════════════════════════════════════
    // 스트리밍 옵션 (네트워크 소스 전용)
    // ════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _rtspTransport = "tcp";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _timeoutSeconds = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _probeSizeKb = 500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private double _analyzeDurationSeconds = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _noBuffer = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _maxDelayMs = 500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _liveThreadCount = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _recvBufferSizeKb = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _reorderQueueSize = 500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _reconnect = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _reconnectDelayMaxSeconds = 0;

    // ════════════════════════════════════════════════════════════════
    // RadioButton / ComboBox 편의 프로퍼티
    // ════════════════════════════════════════════════════════════════

    // 테마
    public bool IsSystemTheme { get => SelectedTheme == AppTheme.System; set { if (value) SelectedTheme = AppTheme.System; } }
    public bool IsLightTheme  { get => SelectedTheme == AppTheme.Light;  set { if (value) SelectedTheme = AppTheme.Light;  } }
    public bool IsDarkTheme   { get => SelectedTheme == AppTheme.Dark;   set { if (value) SelectedTheme = AppTheme.Dark;   } }

    // RTSP 전송 프로토콜
    public bool IsRtspTcp  { get => RtspTransport == "tcp";  set { if (value) RtspTransport = "tcp";  } }
    public bool IsRtspUdp  { get => RtspTransport == "udp";  set { if (value) RtspTransport = "udp";  } }
    public bool IsRtspHttp { get => RtspTransport == "http"; set { if (value) RtspTransport = "http"; } }

    partial void OnRtspTransportChanged(string value)
    {
        OnPropertyChanged(nameof(IsRtspTcp));
        OnPropertyChanged(nameof(IsRtspUdp));
        OnPropertyChanged(nameof(IsRtspHttp));
    }

    // ════════════════════════════════════════════════════════════════
    // 파생 프로퍼티
    // ════════════════════════════════════════════════════════════════

    public bool HasUnsavedChanges
    {
        get
        {
            var s  = _settingsService.Current;
            var sd = s.Decoder;
            var st = s.Streaming;

            return FFmpegBinaryPath    != s.FFmpegBinaryPath    ||
                   DefaultOutputFolder != s.DefaultOutputFolder ||
                   SelectedTheme       != s.Theme               ||
                   SelectedLanguage    != s.Language            ||
                   // 디코더 공통
                   HwAccel            != sd.HwAccel             ||
                   FileThreadCount    != sd.FileThreadCount      ||
                   SkipLoopFilter     != sd.SkipLoopFilter       ||
                   SkipFrame          != sd.SkipFrame            ||
                   // 스트리밍
                   RtspTransport      != st.RtspTransport        ||
                   TimeoutSeconds     != st.TimeoutSeconds       ||
                   ProbeSizeKb        != st.ProbeSizeKb          ||
                   Math.Abs(AnalyzeDurationSeconds - st.AnalyzeDurationSeconds) > 0.001 ||
                   NoBuffer           != st.NoBuffer             ||
                   MaxDelayMs         != st.MaxDelayMs           ||
                   LiveThreadCount    != st.LiveThreadCount      ||
                   RecvBufferSizeKb   != st.RecvBufferSizeKb     ||
                   ReorderQueueSize   != st.ReorderQueueSize     ||
                   Reconnect          != st.Reconnect            ||
                   ReconnectDelayMaxSeconds != st.ReconnectDelayMaxSeconds;
        }
    }

    public string FFmpegPathValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FFmpegBinaryPath)) return string.Empty;
            return Directory.Exists(FFmpegBinaryPath) ? string.Empty : "⚠ 폴더를 찾을 수 없습니다.";
        }
    }

    public string OutputFolderValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DefaultOutputFolder)) return string.Empty;
            return Directory.Exists(DefaultOutputFolder) ? string.Empty : "⚠ 폴더를 찾을 수 없습니다.";
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 생성자
    // ════════════════════════════════════════════════════════════════

    public SettingsViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ISnackbarService snackbarService,
        IFFmpegInitializer ffmpegInitializer,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService   = settingsService;
        _dialogService     = dialogService;
        _snackbarService   = snackbarService;
        _ffmpegInitializer = ffmpegInitializer;
        _logger            = logger;

        LoadFromSettings(_settingsService.Current);
    }

    // ── 테마 변경 즉시 적용 ──────────────────────────────────────────
    partial void OnSelectedThemeChanged(AppTheme value)
    {
        var wpfTheme = value switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark  => ApplicationTheme.Dark,
            _              => ApplicationTheme.Dark
        };
        ApplicationThemeManager.Apply(wpfTheme);
    }

    // ════════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void BrowseFFmpegPath()
    {
        var dialog = new OpenFolderDialog { Title = "FFmpeg 바이너리 폴더를 선택하세요", Multiselect = false };
        if (dialog.ShowDialog() == true) FFmpegBinaryPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "기본 출력 폴더를 선택하세요", Multiselect = false };
        if (dialog.ShowDialog() == true) DefaultOutputFolder = dialog.FolderName;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var prevPath = _settingsService.Current.FFmpegBinaryPath;

            var newSettings = new AppSettings
            {
                FFmpegBinaryPath    = FFmpegBinaryPath,
                DefaultOutputFolder = DefaultOutputFolder,
                Theme               = SelectedTheme,
                Language            = SelectedLanguage,
                Decoder = new DecoderOptions
                {
                    HwAccel        = HwAccel,
                    FileThreadCount = FileThreadCount,
                    SkipLoopFilter = SkipLoopFilter,
                    SkipFrame      = SkipFrame,
                },
                Streaming = new StreamingOptions
                {
                    RtspTransport          = RtspTransport,
                    TimeoutSeconds         = TimeoutSeconds,
                    ProbeSizeKb            = ProbeSizeKb,
                    AnalyzeDurationSeconds = AnalyzeDurationSeconds,
                    NoBuffer               = NoBuffer,
                    MaxDelayMs             = MaxDelayMs,
                    LiveThreadCount        = LiveThreadCount,
                    RecvBufferSizeKb       = RecvBufferSizeKb,
                    ReorderQueueSize       = ReorderQueueSize,
                    Reconnect              = Reconnect,
                    ReconnectDelayMaxSeconds = ReconnectDelayMaxSeconds,
                },
            };

            await _settingsService.SaveAsync(newSettings);
            OnPropertyChanged(nameof(HasUnsavedChanges));

            var pathChanged = !string.Equals(prevPath, FFmpegBinaryPath, StringComparison.OrdinalIgnoreCase);
            if (pathChanged && !string.IsNullOrWhiteSpace(FFmpegBinaryPath))
                await TryReinitializeFFmpegAsync();

            _snackbarService.Show(
                "저장 완료", "설정이 저장되었습니다.",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("저장 실패", "설정 저장 중 오류가 발생했습니다.", ex);
        }
    }

    private async Task TryReinitializeFFmpegAsync()
    {
        try
        {
            await _ffmpegInitializer.InitializeAsync(FFmpegBinaryPath);
            _snackbarService.Show(
                "FFmpeg 로드 완료",
                $"FFmpeg {_ffmpegInitializer.VersionInfo?.AvcodecVersion} 초기화 성공",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(4));
        }
        catch (FFmpegInitializationException ex)
        {
            _logger.LogError(ex, "FFmpeg 재초기화 실패");
            await _dialogService.ShowErrorAsync(
                "FFmpeg 초기화 실패",
                "FFmpeg 바이너리를 로드할 수 없습니다.\n경로를 확인해 주세요.", ex);
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            "기본값으로 초기화",
            "모든 설정을 기본값으로 되돌립니다. 계속하시겠습니까?",
            confirmText: "초기화", cancelText: "취소");

        if (!confirmed) return;

        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        await _settingsService.SaveAsync(defaults);
        OnPropertyChanged(nameof(HasUnsavedChanges));

        _snackbarService.Show(
            "초기화 완료", "설정이 기본값으로 초기화되었습니다.",
            ControlAppearance.Caution,
            new SymbolIcon(SymbolRegular.ArrowReset24),
            TimeSpan.FromSeconds(3));
    }

    // ════════════════════════════════════════════════════════════════
    // 내부 헬퍼
    // ════════════════════════════════════════════════════════════════

    private void LoadFromSettings(AppSettings s)
    {
        FFmpegBinaryPath    = s.FFmpegBinaryPath;
        DefaultOutputFolder = s.DefaultOutputFolder;
        SelectedTheme       = s.Theme;
        SelectedLanguage    = s.Language;

        var sd = s.Decoder;
        HwAccel         = sd.HwAccel;
        FileThreadCount = sd.FileThreadCount;
        SkipLoopFilter  = sd.SkipLoopFilter;
        SkipFrame       = sd.SkipFrame;

        var st = s.Streaming;
        RtspTransport            = st.RtspTransport;
        TimeoutSeconds           = st.TimeoutSeconds;
        ProbeSizeKb              = st.ProbeSizeKb;
        AnalyzeDurationSeconds   = st.AnalyzeDurationSeconds;
        NoBuffer                 = st.NoBuffer;
        MaxDelayMs               = st.MaxDelayMs;
        LiveThreadCount          = st.LiveThreadCount;
        RecvBufferSizeKb         = st.RecvBufferSizeKb;
        ReorderQueueSize         = st.ReorderQueueSize;
        Reconnect                = st.Reconnect;
        ReconnectDelayMaxSeconds = st.ReconnectDelayMaxSeconds;

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
