using System.Text;
using System.Windows;
using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;

namespace fflux.UI.Modules.FFmpegExplorer;

public sealed partial class FFmpegExplorerViewModel : ObservableObject
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IFFmpegCommandService      _commandService;
    private readonly ISettingsService           _settings;
    private readonly ILogger<FFmpegExplorerViewModel> _logger;

    // ── 실행 취소 토큰 ───────────────────────────────────────────────

    private CancellationTokenSource? _executeCts;

    // ────────────────────────────────────────────────────────────────
    // ComboBox 데이터 소스 (static — View에서 ItemsSource로 바인딩)
    // ────────────────────────────────────────────────────────────────

    public static string[] VideoCodecDisplayNames { get; } =
    [
        "copy  (스트림 복사, 무손실)",
        "libx264  (H.264 / AVC)",
        "libx265  (H.265 / HEVC)",
        "libvpx-vp9  (VP9)",
        "libaom-av1  (AV1)",
    ];
    private static readonly string[] VideoCodecValues =
        ["copy", "libx264", "libx265", "libvpx-vp9", "libaom-av1"];

    public static string[] ResolutionDisplayNames { get; } =
    [
        "(원본 유지)",
        "3840×2160  (4K)",
        "1920×1080  (1080p)",
        "1280×720   (720p)",
        "854×480    (480p)",
        "640×360    (360p)",
    ];
    private static readonly string?[] ResolutionValues =
        [null, "3840x2160", "1920x1080", "1280x720", "854x480", "640x360"];

    public static string[] AudioCodecDisplayNames { get; } =
    [
        "copy  (스트림 복사, 무손실)",
        "aac",
        "libmp3lame  (MP3)",
        "libopus  (Opus)",
        "flac",
        "pcm_s16le  (WAV PCM)",
    ];
    private static readonly string[] AudioCodecValues =
        ["copy", "aac", "libmp3lame", "libopus", "flac", "pcm_s16le"];

    public static string[] SampleRateDisplayNames { get; } =
        ["(원본 유지)", "48000 Hz", "44100 Hz", "22050 Hz", "16000 Hz"];
    private static readonly int?[] SampleRateValues =
        [null, 48000, 44100, 22050, 16000];

    public static string[] ChannelDisplayNames { get; } =
        ["(원본 유지)", "1  (Mono)", "2  (Stereo)", "6  (5.1 Surround)"];
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
    [ObservableProperty] private string _statusText   = "준비";
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
                return "(입력·출력 파일을 선택하면 커맨드가 생성됩니다)";
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
    }

    // ────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseInput()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "입력 파일 선택",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
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
            Title  = "출력 파일 저장",
            Filter = "MP4|*.mp4|MKV|*.mkv|WebM|*.webm|모든 파일|*.*",
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
        StatusText = "클립보드에 복사됨";
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteAsync()
    {
        var binaryDir = _settings.Current.FFmpegBinaryPath;
        var ffmpegExe = Path.Combine(binaryDir, "ffmpeg.exe");

        if (!File.Exists(ffmpegExe))
        {
            AppendLog($"[오류] ffmpeg.exe를 찾을 수 없습니다: {ffmpegExe}");
            AppendLog("       설정 페이지에서 FFmpeg 바이너리 경로를 확인하세요.");
            ShowLog    = true;
            StatusText = "FFmpeg 경로 오류";
            return;
        }

        _logBuilder.Clear();
        LogText       = "";
        ProgressValue = 0;
        ProgressText  = "";
        IsRunning     = true;
        ShowLog       = true;
        StatusText    = "실행 중…";

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
                StatusText    = "완료";
                ProgressValue = 100;
                AppendLog(new string('─', 60));
                AppendLog("[완료] 인코딩이 성공적으로 끝났습니다.");
            }
            else
            {
                StatusText = $"오류 (종료 코드: {exitCode})";
                AppendLog($"[오류] ffmpeg 종료 코드: {exitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "취소됨";
            AppendLog("[취소] 사용자에 의해 중단됐습니다.");
        }
        catch (Exception ex)
        {
            StatusText = $"실행 오류: {ex.Message}";
            AppendLog($"[예외] {ex.Message}");
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
        StatusText = "취소 요청…";
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
            ProgressText = $"처리 중: {p.CurrentTime:hh\\:mm\\:ss\\.ff}";
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
