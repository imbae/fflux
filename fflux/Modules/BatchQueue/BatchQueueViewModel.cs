using System.Collections.ObjectModel;
using System.Collections.Specialized;
using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;

namespace fflux.UI.Modules.BatchQueue;

public sealed partial class BatchQueueViewModel : ObservableObject
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IFFmpegCommandService _commandService;
    private readonly ISettingsService      _settings;
    private readonly ILogger<BatchQueueViewModel> _logger;

    private CancellationTokenSource? _cts;

    // ── 큐 ──────────────────────────────────────────────────────────

    public ObservableCollection<BatchJobViewModel> Jobs { get; } = [];

    // ── 인코딩 프리셋 (공유 옵션) ────────────────────────────────────

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
        "(원본 유지)", "3840×2160  (4K)", "1920×1080  (1080p)",
        "1280×720   (720p)", "854×480    (480p)", "640×360    (360p)",
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
    ];
    private static readonly string[] AudioCodecValues =
        ["copy", "aac", "libmp3lame", "libopus", "flac"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoEncodeEnabled))]
    private int _videoCodecIndex = 0;

    [ObservableProperty] private int    _crfValue         = 23;
    [ObservableProperty] private string _videoBitrateText  = "";
    [ObservableProperty] private bool   _useCrf            = true;
    [ObservableProperty] private int    _resolutionIndex   = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioEncodeEnabled))]
    private int _audioCodecIndex = 0;

    [ObservableProperty] private string _audioBitrateText = "";

    public bool IsVideoEncodeEnabled => VideoCodecIndex >= 1;
    public bool IsAudioEncodeEnabled => AudioCodecIndex >= 1;

    // ── 실행 상태 ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _summaryText = "작업 없음";

    // ────────────────────────────────────────────────────────────────
    // 생성자
    // ────────────────────────────────────────────────────────────────

    public BatchQueueViewModel(
        IFFmpegCommandService        commandService,
        ISettingsService             settings,
        ILogger<BatchQueueViewModel> logger)
    {
        _commandService = commandService;
        _settings       = settings;
        _logger         = logger;

        Jobs.CollectionChanged += OnJobsChanged;
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        StartQueueCommand.NotifyCanExecuteChanged();
        ClearDoneCommand .NotifyCanExecuteChanged();
        UpdateSummary();
    }

    // ────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddFiles))]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title       = "인코딩할 파일 선택 (다중 선택 가능)",
            Filter      = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true)
            foreach (var path in dlg.FileNames)
                AddJobInternal(path);
    }

    private bool CanAddFiles() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStartQueue))]
    private async Task StartQueueAsync()
    {
        var binaryDir = _settings.Current.FFmpegBinaryPath;
        var ffmpegExe = Path.Combine(binaryDir, "ffmpeg.exe");

        if (!File.Exists(ffmpegExe))
        {
            SummaryText = $"오류: ffmpeg.exe를 찾을 수 없습니다 — {ffmpegExe}";
            return;
        }

        IsRunning = true;
        _cts      = new CancellationTokenSource();

        foreach (var job in Jobs.Where(j => !j.IsFinished).ToList())
        {
            if (_cts.IsCancellationRequested) break;

            job.Reset();
            job.Status = BatchJobStatus.Running;

            var options  = BuildOptions(job);
            var progress = new Progress<FFmpegProgress>(p => OnJobProgress(job, p));

            try
            {
                job.AppendLog($"$ ffmpeg {_commandService.BuildArguments(options)}");
                job.AppendLog(new string('─', 60));

                var code = await _commandService.ExecuteAsync(
                    ffmpegExe, options, progress, _cts.Token);

                job.Status = code == 0 ? BatchJobStatus.Done : BatchJobStatus.Failed;
                if (code != 0)
                    job.AppendLog($"[오류] 종료 코드: {code}");
                else
                    job.AppendLog("[완료] 성공적으로 인코딩됐습니다.");
            }
            catch (OperationCanceledException)
            {
                job.Status = BatchJobStatus.Cancelled;
                job.AppendLog("[취소] 사용자에 의해 중단됐습니다.");
                break;
            }
            catch (Exception ex)
            {
                job.Status = BatchJobStatus.Failed;
                job.AppendLog($"[예외] {ex.Message}");
                _logger.LogError(ex, "배치 인코딩 실패: {File}", job.InputFilePath);
            }

            UpdateSummary();
        }

        IsRunning = false;
        _cts?.Dispose();
        _cts = null;
        UpdateSummary();
    }

    private bool CanStartQueue() => !IsRunning && Jobs.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStopQueue))]
    private void StopQueue()
    {
        _cts?.Cancel();
        SummaryText = "취소 요청 중…";
    }

    private bool CanStopQueue() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanClearDone))]
    private void ClearDone()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
            Jobs.Remove(job);
    }

    private bool CanClearDone() => !IsRunning && Jobs.Any(j => j.IsFinished);

    [RelayCommand]
    private void RemoveJob(BatchJobViewModel job)
    {
        if (IsRunning && !job.IsFinished && job.Status != BatchJobStatus.Pending)
            return;
        Jobs.Remove(job);
    }

    [RelayCommand]
    private void ResetJob(BatchJobViewModel job)
    {
        if (IsRunning) return;
        job.Reset();
        UpdateSummary();
        StartQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleLog(BatchJobViewModel job) => job.ShowLog = !job.ShowLog;

    // ────────────────────────────────────────────────────────────────
    // 헬퍼
    // ────────────────────────────────────────────────────────────────

    /// <summary>외부(드래그 드롭 등)에서 파일을 큐에 추가합니다.</summary>
    public void AddJobFromPath(string inputPath) => AddJobInternal(inputPath);

    private void AddJobInternal(string inputPath)
    {
        // 중복 체크
        if (Jobs.Any(j => j.InputFilePath.Equals(inputPath, StringComparison.OrdinalIgnoreCase)))
            return;

        var outputPath = SuggestOutputPath(inputPath);
        Jobs.Add(new BatchJobViewModel(inputPath, outputPath));
    }

    private static void OnJobProgress(BatchJobViewModel job, FFmpegProgress p)
    {
        if (p.LogLine  != null)    job.AppendLog(p.LogLine);
        if (p.Percent  is { } pct) job.Progress = pct;
    }

    private FFmpegCommandOptions BuildOptions(BatchJobViewModel job) => new()
    {
        InputFile  = job.InputFilePath,
        OutputFile = job.OutputFilePath,

        VideoCodec   = VideoCodecIndex < VideoCodecValues.Length
                       ? VideoCodecValues[VideoCodecIndex] : null,
        Crf          = IsVideoEncodeEnabled && UseCrf && CrfValue > 0         ? CrfValue : null,
        VideoBitrate = IsVideoEncodeEnabled && !UseCrf
                       && int.TryParse(VideoBitrateText, out int vbr) && vbr > 0 ? vbr : null,
        Resolution   = IsVideoEncodeEnabled && ResolutionIndex > 0
                       && ResolutionIndex < ResolutionValues.Length
                       ? ResolutionValues[ResolutionIndex] : null,

        AudioCodec   = AudioCodecIndex < AudioCodecValues.Length
                       ? AudioCodecValues[AudioCodecIndex] : null,
        AudioBitrate = IsAudioEncodeEnabled
                       && int.TryParse(AudioBitrateText, out int abr) && abr > 0 ? abr : null,
    };

    private void UpdateSummary()
    {
        if (Jobs.Count == 0) { SummaryText = "작업 없음"; return; }

        int pending   = Jobs.Count(j => j.Status == BatchJobStatus.Pending);
        int running   = Jobs.Count(j => j.Status == BatchJobStatus.Running);
        int done      = Jobs.Count(j => j.Status == BatchJobStatus.Done);
        int failed    = Jobs.Count(j => j.Status == BatchJobStatus.Failed);
        int cancelled = Jobs.Count(j => j.Status == BatchJobStatus.Cancelled);

        var parts = new List<string>();
        if (pending   > 0) parts.Add($"대기 {pending}개");
        if (running   > 0) parts.Add($"실행 중 {running}개");
        if (done      > 0) parts.Add($"완료 {done}개");
        if (failed    > 0) parts.Add($"실패 {failed}개");
        if (cancelled > 0) parts.Add($"취소 {cancelled}개");
        SummaryText = string.Join("  |  ", parts);
    }

    private static string SuggestOutputPath(string inputPath)
    {
        var dir  = Path.GetDirectoryName(inputPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{stem}_encoded.mp4");
    }
}
