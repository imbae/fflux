using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Shared.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;

namespace fflux.UI.Modules.BitrateAnalyzer;

public sealed partial class BitrateAnalyzerViewModel : ObservableObject
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IBitrateAnalysisService _analysisService;
    private readonly ISettingsService        _settings;
    private readonly ILogger<BitrateAnalyzerViewModel> _logger;

    private CancellationTokenSource? _cts;

    // ── 입력 / 상태 ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private string _filePath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isAnalyzing;

    [ObservableProperty] private bool   _hasResult;
    [ObservableProperty] private string _statusText     = "파일을 선택하세요.";
    [ObservableProperty] private string _stageText      = "";
    [ObservableProperty] private int    _framesProcessed;

    // ── 통계 프로퍼티 ────────────────────────────────────────────────

    [ObservableProperty] private string _avgBitrateText       = "—";
    [ObservableProperty] private string _peakBitrateText      = "—";
    [ObservableProperty] private string _minBitrateText       = "—";
    [ObservableProperty] private string _frameTypeText        = "—";
    [ObservableProperty] private string _avgGopText           = "—";
    [ObservableProperty] private string _durationText         = "—";
    [ObservableProperty] private string _totalFramesText      = "—";

    // ── LiveCharts 바인딩 ─────────────────────────────────────────────

    [ObservableProperty] private ISeries[] _series = [];
    [ObservableProperty] private Axis[]    _xAxes  = [new Axis { Name = "시간 (초)", NamePaint = new SolidColorPaint(SKColors.Gray) }];
    [ObservableProperty] private Axis[]    _yAxes  = [new Axis { Name = "비트레이트 (kbps)", NamePaint = new SolidColorPaint(SKColors.Gray) }];

    // ────────────────────────────────────────────────────────────────
    // 생성자
    // ────────────────────────────────────────────────────────────────

    public BitrateAnalyzerViewModel(
        IBitrateAnalysisService            analysisService,
        ISettingsService                   settings,
        ILogger<BitrateAnalyzerViewModel>  logger)
    {
        _analysisService = analysisService;
        _settings        = settings;
        _logger          = logger;
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

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        var binaryDir  = _settings.Current.FFmpegBinaryPath;
        var ffprobeExe = Path.Combine(binaryDir, "ffprobe.exe");

        if (!File.Exists(ffprobeExe))
        {
            StatusText = $"오류: ffprobe.exe를 찾을 수 없습니다 — {ffprobeExe}";
            return;
        }

        IsAnalyzing      = true;
        HasResult        = false;
        FramesProcessed  = 0;
        StageText        = "";
        StatusText       = "분석 중…";
        _cts = new CancellationTokenSource();

        var progress = new Progress<(string Stage, int Frames)>(p =>
        {
            StageText       = p.Stage;
            FramesProcessed = p.Frames;
        });

        try
        {
            var result = await _analysisService.AnalyzeAsync(
                ffprobeExe, FilePath, progress, _cts.Token);

            UpdateStats(result);
            UpdateChart(result);

            FramesProcessed = (int)result.TotalFrames;
            HasResult  = true;
            StatusText = $"분석 완료 — {result.TotalFrames:N0}개 프레임 · {result.Duration:hh\\:mm\\:ss}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "취소됨";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
            _logger.LogError(ex, "비트레이트 분석 실패: {File}", FilePath);
        }
        finally
        {
            IsAnalyzing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(FilePath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "취소 요청 중…";
    }

    private bool CanCancel() => IsAnalyzing;

    // ────────────────────────────────────────────────────────────────
    // 헬퍼
    // ────────────────────────────────────────────────────────────────

    private void UpdateStats(BitrateAnalysisResult r)
    {
        AvgBitrateText  = FormatKbps(r.AverageVideoKbps);
        PeakBitrateText = FormatKbps(r.PeakVideoKbps);
        MinBitrateText  = FormatKbps(r.MinVideoKbps);
        FrameTypeText   = r.FrameTypeRatioText;
        AvgGopText      = r.AvgGopSizeText;
        DurationText    = r.Duration.ToString(@"hh\:mm\:ss");
        TotalFramesText = $"{r.TotalFrames:N0}";
    }

    private void UpdateChart(BitrateAnalysisResult r)
    {
        // X축 레이블: 초 → MM:SS 포맷
        XAxes =
        [
            new Axis
            {
                Name      = "시간",
                NamePaint = new SolidColorPaint(SKColors.Gray),
                Labeler   = val => TimeSpan.FromSeconds(val).ToString(@"mm\:ss"),
                TextSize  = 11,
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name       = "비트레이트 (kbps)",
                NamePaint  = new SolidColorPaint(SKColors.Gray),
                Labeler    = val => $"{val:N0}",
                TextSize   = 11,
                MinLimit   = 0,
            }
        ];

        var seriesList = new List<ISeries>();

        // 비디오 시리즈 (파란 영역 라인)
        if (r.VideoPerSecond.Count > 0)
        {
            var videoValues = r.VideoPerSecond
                .Select(p => new ObservablePoint(p.TimeSeconds, p.BitrateKbps))
                .ToArray();

            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name            = "비디오 (kbps)",
                Values          = videoValues,
                Stroke          = new SolidColorPaint(new SKColor(0x42, 0x90, 0xF5), 2),
                Fill            = new SolidColorPaint(new SKColor(0x42, 0x90, 0xF5, 50)),
                GeometrySize    = 0,
                GeometryStroke  = null,
                GeometryFill    = null,
                LineSmoothness  = 0,
            });
        }

        // 오디오 시리즈 (주황 라인, fill 없음)
        if (r.AudioPerSecond.Count > 0)
        {
            var audioValues = r.AudioPerSecond
                .Select(p => new ObservablePoint(p.TimeSeconds, p.BitrateKbps))
                .ToArray();

            seriesList.Add(new LineSeries<ObservablePoint>
            {
                Name            = "오디오 (kbps)",
                Values          = audioValues,
                Stroke          = new SolidColorPaint(new SKColor(0xFF, 0x99, 0x00), 2),
                Fill            = null,
                GeometrySize    = 0,
                GeometryStroke  = null,
                GeometryFill    = null,
                LineSmoothness  = 0,
            });
        }

        Series = [.. seriesList];
    }

    private static string FormatKbps(double kbps)
        => kbps >= 1000
            ? $"{kbps / 1000.0:F2} Mbps"
            : $"{kbps:F0} kbps";

    /// <summary>드래그 앤 드롭으로 파일 경로를 설정합니다.</summary>
    public void SetFilePath(string path) => FilePath = path;
}
