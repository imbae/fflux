using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;

namespace fflux.UI.Modules.SceneDetector;

public sealed partial class SceneDetectorViewModel : ObservableObject
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly ISceneDetectionService _detectionService;
    private readonly ISettingsService       _settings;
    private readonly ILogger<SceneDetectorViewModel> _logger;

    private CancellationTokenSource? _cts;
    private TimeSpan _totalDuration = TimeSpan.Zero;

    // ────────────────────────────────────────────────────────────────
    // 입력 프로퍼티
    // ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DetectCommand))]
    private string _filePath = "";

    /// <summary>감지 임계값 (0.0 ~ 1.0). 낮을수록 민감.</summary>
    [ObservableProperty] private double _threshold = 0.40;

    // ────────────────────────────────────────────────────────────────
    // 실행 상태
    // ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DetectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportFfmetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    private bool _isDetecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportFfmetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    private bool _hasResult;

    [ObservableProperty] private string _statusText  = "파일을 선택하세요.";
    [ObservableProperty] private int    _scenesFound;

    // ────────────────────────────────────────────────────────────────
    // 결과
    // ────────────────────────────────────────────────────────────────

    public ObservableCollection<ChapterEntry> Chapters { get; } = [];

    // ────────────────────────────────────────────────────────────────
    // 생성자
    // ────────────────────────────────────────────────────────────────

    public SceneDetectorViewModel(
        ISceneDetectionService           detectionService,
        ISettingsService                 settings,
        ILogger<SceneDetectorViewModel>  logger)
    {
        _detectionService = detectionService;
        _settings         = settings;
        _logger           = logger;
    }

    // ────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "분석할 미디어 파일 선택",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
        };
        if (dlg.ShowDialog() == true)
            FilePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanDetect))]
    private async Task DetectAsync()
    {
        var binaryDir = _settings.Current.FFmpegBinaryPath;
        var ffmpegExe = Path.Combine(binaryDir, "ffmpeg.exe");

        if (!File.Exists(ffmpegExe))
        {
            StatusText = $"오류: ffmpeg.exe를 찾을 수 없습니다 — {ffmpegExe}";
            return;
        }

        // 초기화
        IsDetecting  = true;
        HasResult    = false;
        ScenesFound  = 0;
        StatusText   = "분석 중… (파일 크기에 따라 수 분이 걸릴 수 있습니다)";
        Chapters.Clear();
        _totalDuration = TimeSpan.Zero;
        _cts = new CancellationTokenSource();

        var progress = new Progress<SceneDetectionProgress>(p =>
        {
            ScenesFound = p.ScenesFound;
            if (p.LatestSceneTime.HasValue)
                StatusText = $"분석 중… 장면 {p.ScenesFound}개 발견  " +
                             $"(마지막: {FormatTs(p.LatestSceneTime.Value)})";
        });

        try
        {
            var result = await _detectionService.DetectScenesAsync(
                ffmpegExe, FilePath, Threshold, progress, _cts.Token);

            _totalDuration = result.TotalDuration;
            BuildChapters(result.Scenes);

            HasResult  = true;
            StatusText = Chapters.Count > 0
                ? $"감지 완료 — 챕터 {Chapters.Count}개 (전체 길이 {FormatTs(_totalDuration)})"
                : "감지 완료 — 장면 전환 없음 (임계값을 낮춰보세요)";
        }
        catch (OperationCanceledException)
        {
            StatusText = "취소됨";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
            _logger.LogError(ex, "장면 감지 실패: {File}", FilePath);
        }
        finally
        {
            IsDetecting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanDetect() => !IsDetecting && !string.IsNullOrWhiteSpace(FilePath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "취소 요청 중…";
    }

    private bool CanCancel() => IsDetecting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportFfmetadata()
    {
        var dlg = new SaveFileDialog
        {
            Title      = "FFmpeg 메타데이터 파일 저장",
            Filter     = "FFmpeg Metadata|*.txt|모든 파일|*.*",
            FileName   = "chapters.txt",
            DefaultExt = ".txt",
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, BuildFfmetadata(), Encoding.UTF8);
        StatusText = $"저장됨: {dlg.FileName}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportText()
    {
        var dlg = new SaveFileDialog
        {
            Title      = "챕터 텍스트 파일 저장",
            Filter     = "텍스트 파일|*.txt|모든 파일|*.*",
            FileName   = "chapters_simple.txt",
            DefaultExt = ".txt",
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, BuildSimpleText(), Encoding.UTF8);
        StatusText = $"저장됨: {dlg.FileName}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void CopyToClipboard()
    {
        Clipboard.SetText(BuildFfmetadata());
        StatusText = "FFmpeg 메타데이터를 클립보드에 복사했습니다.";
    }

    private bool CanExport() => !IsDetecting && HasResult && Chapters.Count > 0;

    // ────────────────────────────────────────────────────────────────
    // 헬퍼
    // ────────────────────────────────────────────────────────────────

    /// <summary>감지 결과로부터 챕터 목록을 구성합니다. 시작(00:00:00)을 첫 챕터로 추가합니다.</summary>
    private void BuildChapters(IReadOnlyList<SceneEntry> scenes)
    {
        Chapters.Clear();

        // 첫 챕터: 영상 시작점 (항상 포함)
        Chapters.Add(new ChapterEntry(1, TimeSpan.Zero, 0));

        int num = 2;
        foreach (var s in scenes)
            Chapters.Add(new ChapterEntry(num++, s.Timestamp, s.Score));
    }

    /// <summary>FFmpeg -i 명령에 사용할 ;FFMETADATA1 포맷 문자열을 생성합니다.</summary>
    private string BuildFfmetadata()
    {
        var sb = new StringBuilder();
        sb.AppendLine(";FFMETADATA1");
        sb.AppendLine();

        long totalMs = _totalDuration == TimeSpan.Zero
            ? long.MaxValue / 2           // 알 수 없으면 충분히 큰 값
            : (long)_totalDuration.TotalMilliseconds;

        for (int i = 0; i < Chapters.Count; i++)
        {
            var chapter = Chapters[i];
            long startMs = (long)chapter.Start.TotalMilliseconds;
            long endMs   = i + 1 < Chapters.Count
                ? (long)Chapters[i + 1].Start.TotalMilliseconds - 1
                : totalMs;

            sb.AppendLine("[CHAPTER]");
            sb.AppendLine("TIMEBASE=1/1000");
            sb.AppendLine($"START={startMs}");
            sb.AppendLine($"END={endMs}");
            sb.AppendLine($"title={chapter.Title}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>"HH:MM:SS 제목" 형식의 간단한 텍스트를 생성합니다.</summary>
    private string BuildSimpleText()
    {
        var sb = new StringBuilder();
        foreach (var c in Chapters)
            sb.AppendLine($"{c.TimestampText}  {c.Title}");
        return sb.ToString();
    }

    private static string FormatTs(TimeSpan ts)
        => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
}
