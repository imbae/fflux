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

    // 언어
    public bool IsKoreanLanguage { get => SelectedLanguage == AppLanguage.Korean;  set { if (value) SelectedLanguage = AppLanguage.Korean;  } }
    public bool IsEnglishLanguage{ get => SelectedLanguage == AppLanguage.English; set { if (value) SelectedLanguage = AppLanguage.English; } }

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

    private static LocalizationManager Loc => LocalizationManager.Instance;

    public string FFmpegPathValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FFmpegBinaryPath)) return string.Empty;
            return Directory.Exists(FFmpegBinaryPath) ? string.Empty : Loc["Settings.Validation.FolderNotFound"];
        }
    }

    public string OutputFolderValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DefaultOutputFolder)) return string.Empty;
            return Directory.Exists(DefaultOutputFolder) ? string.Empty : Loc["Settings.Validation.FolderNotFound"];
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

    // ── 언어 변경 즉시 적용 ──────────────────────────────────────────
    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        LocalizationManager.Instance.SetLanguage(value);
        OnPropertyChanged(nameof(IsKoreanLanguage));
        OnPropertyChanged(nameof(IsEnglishLanguage));
    }

    // ════════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════════

    // ── 파일/폴더 찾아보기 ───────────────────────────────────────────

    [RelayCommand]
    private void BrowseFFmpegPath()
    {
        var dialog = new OpenFolderDialog { Title = Loc["Dialog.Browse.FFmpeg"], Multiselect = false };
        if (dialog.ShowDialog() == true) FFmpegBinaryPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc["Dialog.Browse.OutputFolder"], Multiselect = false };
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
                Loc["Dialog.Save.Title"], Loc["Dialog.Save.Message"],
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(Loc["Dialog.SaveFailed"], Loc["Dialog.SaveFailedMsg"], ex);
        }
    }

    private async Task TryReinitializeFFmpegAsync()
    {
        try
        {
            await _ffmpegInitializer.InitializeAsync(FFmpegBinaryPath);
            _snackbarService.Show(
                Loc["Dialog.FFmpeg.Success"],
                string.Format(Loc["Dialog.FFmpeg.SuccessMsg"], _ffmpegInitializer.VersionInfo?.AvcodecVersion),
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(4));
        }
        catch (FFmpegInitializationException ex)
        {
            _logger.LogError(ex, "FFmpeg 재초기화 실패");
            await _dialogService.ShowErrorAsync(
                Loc["Dialog.FFmpeg.Failed"],
                Loc["Dialog.FFmpeg.FailedMsg"], ex);
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            Loc["Dialog.Reset.Title"],
            Loc["Dialog.Reset.Message"],
            confirmText: Loc["Dialog.Reset.Confirm"],
            cancelText:  Loc["Dialog.Reset.Cancel"]);

        if (!confirmed) return;

        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        await _settingsService.SaveAsync(defaults);
        OnPropertyChanged(nameof(HasUnsavedChanges));

        _snackbarService.Show(
            Loc["Dialog.Reset.Done"], Loc["Dialog.Reset.DoneMsg"],
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
