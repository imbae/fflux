using System.ComponentModel;
using System.Text;
using System.Windows;
using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;

namespace fflux.UI.Modules.FFmpegExplorer;

public sealed partial class FFmpegExplorerViewModel : ObservableObject
{
    private static LocalizationManager Loc => LocalizationManager.Instance;
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IFFmpegCommandService      _commandService;
    private readonly ISettingsService           _settings;
    private readonly ILogger<FFmpegExplorerViewModel> _logger;

    // ── 실행 취소 토큰 ───────────────────────────────────────────────

    private CancellationTokenSource? _executeCts;

    // ────────────────────────────────────────────────────────────────
    // ComboBox 데이터 소스 (인스턴스 프로퍼티 — 언어 전환 시 재빌드)
    // ────────────────────────────────────────────────────────────────

    public string[] VideoCodecDisplayNames =>
    [
        Loc["FFmpegExplorer.Codec.Copy"],
        "libx264  (H.264 / AVC)",
        "libx265  (H.265 / HEVC)",
        "libvpx-vp9  (VP9)",
        "libaom-av1  (AV1)",
    ];
    private static readonly string[] VideoCodecValues =
        ["copy", "libx264", "libx265", "libvpx-vp9", "libaom-av1"];

    public string[] ResolutionDisplayNames =>
    [
        Loc["FFmpegExplorer.Keep.Original"],
        "3840×2160  (4K)",
        "1920×1080  (1080p)",
        "1280×720   (720p)",
        "854×480    (480p)",
        "640×360    (360p)",
    ];
    private static readonly string?[] ResolutionValues =
        [null, "3840x2160", "1920x1080", "1280x720", "854x480", "640x360"];

    public string[] AudioCodecDisplayNames =>
    [
        Loc["FFmpegExplorer.Codec.Copy"],
        "aac",
        "libmp3lame  (MP3)",
        "libopus  (Opus)",
        "flac",
        "pcm_s16le  (WAV PCM)",
    ];
    private static readonly string[] AudioCodecValues =
        ["copy", "aac", "libmp3lame", "libopus", "flac", "pcm_s16le"];

    public string[] SampleRateDisplayNames =>
        [Loc["FFmpegExplorer.Keep.Original"], "48000 Hz", "44100 Hz", "22050 Hz", "16000 Hz"];
    private static readonly int?[] SampleRateValues =
        [null, 48000, 44100, 22050, 16000];

    public string[] ChannelDisplayNames =>
        [Loc["FFmpegExplorer.Keep.Original"], "1  (Mono)", "2  (Stereo)", "6  (5.1 Surround)"];
    private static readonly int?[] ChannelValues = [null, 1, 2, 6];

    // ────────────────────────────────────────────────────────────────
    // ObservableProperties — 입력
    // ────────────────────────────────────────────────────────────────

    // 파일
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    private string _inputFilePath = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    private string _outputFilePath = "";

    // 비디오
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    [NotifyPropertyChangedFor(nameof(IsVideoEncodeEnabled))]
    private int _videoCodecIndex = 0; // copy

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private int _crfValue = 23;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _videoBitrateText = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _fpsText = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private int _resolutionIndex = 0; // 원본 유지

    /// <summary>true이면 CRF 사용, false이면 비트레이트 사용.</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private bool _useCrf = true;

    // 오디오
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    [NotifyPropertyChangedFor(nameof(IsAudioEncodeEnabled))]
    private int _audioCodecIndex = 0; // copy

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _audioBitrateText = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private int _sampleRateIndex = 0;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private int _channelsIndex = 0;

    // 필터
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _videoFilterText = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _audioFilterText = "";

    // 고급
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GeneratedCommand))]
    private string _extraArgsText = "";

    // ────────────────────────────────────────────────────────────────
    // ObservableProperties — 실행 상태
    // ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _statusText   = "";
    [ObservableProperty] private bool   _showLog;

    private readonly StringBuilder _logBuilder = new();

    [ObservableProperty] private string _logText = "";

    // ────────────────────────────────────────────────────────────────
    // 계산 프로퍼티
    // ────────────────────────────────────────────────────────────────

    /// <summary>인코딩 옵션이 활성화되는 조건 (copy 외 실제 코덱 선택 시).</summary>
    public bool IsVideoEncodeEnabled => VideoCodecIndex >= 1;

    /// <summary>오디오 인코딩 옵션 활성화.</summary>
    public bool IsAudioEncodeEnabled => AudioCodecIndex >= 1;

    /// <summary>현재 설정으로 생성된 ffmpeg 커맨드 전체 (읽기 전용).</summary>
    public string GeneratedCommand
    {
        get
        {
            if (string.IsNullOrEmpty(InputFilePath) && string.IsNullOrEmpty(OutputFilePath))
                return Loc["FFmpegExplorer.Command.Hint"];
            return "ffmpeg " + _commandService.BuildArguments(BuildOptions());
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 생성자
    // ────────────────────────────────────────────────────────────────

    public FFmpegExplorerViewModel(
        IFFmpegCommandService           commandService,
        ISettingsService                settings,
        ILogger<FFmpegExplorerViewModel> logger)
    {
        _commandService = commandService;
        _settings       = settings;
        _logger         = logger;

        StatusText = Loc["FFmpegExplorer.Status.Ready"];
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        StatusText = Loc["FFmpegExplorer.Status.Ready"];
        OnPropertyChanged(nameof(VideoCodecDisplayNames));
        OnPropertyChanged(nameof(ResolutionDisplayNames));
        OnPropertyChanged(nameof(AudioCodecDisplayNames));
        OnPropertyChanged(nameof(SampleRateDisplayNames));
        OnPropertyChanged(nameof(ChannelDisplayNames));
        OnPropertyChanged(nameof(GeneratedCommand));
    }

    // ────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseInput()
    {
        var dlg = new OpenFileDialog
        {
            Title  = Loc["FFmpegExplorer.Dialog.Input"],
            Filter = Loc["FFmpegExplorer.Dialog.InputFilter"],
        };
        if (dlg.ShowDialog() == true)
        {
            InputFilePath = dlg.FileName;
            // 출력 파일 이름 자동 제안
            if (string.IsNullOrEmpty(OutputFilePath))
                OutputFilePath = SuggestOutputPath(dlg.FileName) ?? "";
        }
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new SaveFileDialog
        {
            Title  = Loc["FFmpegExplorer.Dialog.Output"],
            Filter = Loc["FFmpegExplorer.Dialog.OutputFilter"],
        };
        if (!string.IsNullOrEmpty(InputFilePath))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(InputFilePath);
            dlg.FileName         = Path.GetFileName(SuggestOutputPath(InputFilePath));
        }
        if (dlg.ShowDialog() == true)
            OutputFilePath = dlg.FileName ?? "";
    }

    [RelayCommand]
    private void CopyCommand()
    {
        Clipboard.SetText(GeneratedCommand);
        StatusText = Loc["FFmpegExplorer.Copy.Done"];
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteAsync()
    {
        var binaryDir = _settings.Current.FFmpegBinaryPath;
        var ffmpegExe = Path.Combine(binaryDir, "ffmpeg.exe");

        if (!File.Exists(ffmpegExe))
        {
            AppendLog(string.Format(Loc["FFmpegExplorer.Error.NotFound"], ffmpegExe));
            AppendLog("       " + Loc["FFmpegExplorer.Error.CheckSettings"]);
            ShowLog    = true;
            StatusText = Loc["FFmpegExplorer.Status.PathError"];
            return;
        }

        _logBuilder.Clear();
        LogText       = "";
        ProgressValue = 0;
        ProgressText  = "";
        IsRunning     = true;
        ShowLog       = true;
        StatusText    = Loc["FFmpegExplorer.Execute.Label"] + "…";

        AppendLog($"$ ffmpeg {_commandService.BuildArguments(BuildOptions())}");
        AppendLog(new string('─', 60));

        _executeCts = new CancellationTokenSource();
        var progress = new Progress<FFmpegProgress>(OnProgress);

        try
        {
            var exitCode = await _commandService.ExecuteAsync(
                ffmpegExe, BuildOptions(), progress, _executeCts.Token);

            if (exitCode == 0)
            {
                StatusText    = Loc["FFmpegExplorer.Execute.Label"];
                ProgressValue = 100;
                AppendLog(new string('─', 60));
                AppendLog(Loc["FFmpegExplorer.Log.Success"]);
            }
            else
            {
                StatusText = string.Format(Loc["FFmpegExplorer.Log.Error"], exitCode);
                AppendLog(string.Format(Loc["FFmpegExplorer.Log.Error"], exitCode));
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc["FFmpegExplorer.Cancel.Label"];
            AppendLog(Loc["FFmpegExplorer.Log.Cancelled"]);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            AppendLog(string.Format(Loc["FFmpegExplorer.Log.Exception"], ex.Message));
            _logger.LogError(ex, "FFmpeg 실행 실패");
        }
        finally
        {
            IsRunning = false;
            _executeCts?.Dispose();
            _executeCts = null;
        }
    }

    private bool CanExecute() =>
        !IsRunning
        && !string.IsNullOrWhiteSpace(InputFilePath)
        && !string.IsNullOrWhiteSpace(OutputFilePath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _executeCts?.Cancel();
        StatusText = Loc["FFmpegExplorer.Status.Cancelling"];
    }

    private bool CanCancel() => IsRunning;

    // ────────────────────────────────────────────────────────────────
    // 헬퍼
    // ────────────────────────────────────────────────────────────────

    private void OnProgress(FFmpegProgress p)
    {
        if (p.LogLine != null)
            AppendLog(p.LogLine);

        if (p.Percent.HasValue)
            ProgressValue = p.Percent.Value;

        if (p.CurrentTime.HasValue)
            ProgressText = string.Format(Loc["FFmpegExplorer.Progress.Format"], p.CurrentTime.Value.ToString(@"hh\:mm\:ss\.ff"));
    }

    private void AppendLog(string line)
    {
        _logBuilder.AppendLine(line);
        LogText = _logBuilder.ToString();
    }

    private FFmpegCommandOptions BuildOptions() => new()
    {
        InputFile  = InputFilePath,
        OutputFile = OutputFilePath,

        VideoCodec   = VideoCodecIndex < VideoCodecValues.Length ? VideoCodecValues[VideoCodecIndex] : null,
        Crf          = IsVideoEncodeEnabled && UseCrf && CrfValue > 0                            ? CrfValue : null,
        VideoBitrate = IsVideoEncodeEnabled && !UseCrf
                       && int.TryParse(VideoBitrateText, out int vbr) && vbr > 0               ? vbr : null,
        Fps          = IsVideoEncodeEnabled
                       && double.TryParse(FpsText, System.Globalization.NumberStyles.Any,
                          System.Globalization.CultureInfo.InvariantCulture, out double fps)
                       && fps > 0                                                                ? fps : null,
        Resolution   = IsVideoEncodeEnabled && ResolutionIndex > 0
                       && ResolutionIndex < ResolutionValues.Length ? ResolutionValues[ResolutionIndex] : null,

        AudioCodec      = AudioCodecIndex < AudioCodecValues.Length ? AudioCodecValues[AudioCodecIndex] : null,
        AudioBitrate    = IsAudioEncodeEnabled
                          && int.TryParse(AudioBitrateText, out int abr) && abr > 0             ? abr : null,
        AudioSampleRate = IsAudioEncodeEnabled && SampleRateIndex > 0
                          && SampleRateIndex < SampleRateValues.Length
                          ? SampleRateValues[SampleRateIndex]                                    : null,
        AudioChannels   = IsAudioEncodeEnabled && ChannelsIndex > 0
                          && ChannelsIndex < ChannelValues.Length
                          ? ChannelValues[ChannelsIndex]                                         : null,

        VideoFilter = VideoFilterText.Trim().NullIfEmpty(),
        AudioFilter = AudioFilterText.Trim().NullIfEmpty(),
        ExtraArgs   = ExtraArgsText.Trim().NullIfEmpty(),
    };

    private static string? SuggestOutputPath(string inputPath)
    {
        var dir  = Path.GetDirectoryName(inputPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{stem}_output.mp4");
    }
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string s)
        => string.IsNullOrEmpty(s) ? null : s;
}
