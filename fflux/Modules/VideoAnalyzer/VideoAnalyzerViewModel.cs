using System.Text;
using System.Windows;
using fflux.Core.Abstractions;
using fflux.Core.Models.StreamInfo;
using Microsoft.Win32;

namespace fflux.UI.Modules.VideoAnalyzer;

public sealed partial class VideoAnalyzerViewModel : ObservableObject
{
    private readonly IMediaFileReader _reader;
    private readonly ILogger<VideoAnalyzerViewModel> _logger;

    // ── 입력 ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private string _filePath = "";

    // ── 결과 ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideoStreams))]
    [NotifyPropertyChangedFor(nameof(HasAudioStreams))]
    [NotifyPropertyChangedFor(nameof(HasSubtitleStreams))]
    private MediaInfo? _mediaInfo;

    // ── 상태 ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyReportCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyReportCommand))]
    private bool _hasResult;

    [ObservableProperty] private string _statusText = "파일을 드래그하거나 열기 버튼을 누르세요.";

    public bool HasVideoStreams    => MediaInfo?.VideoStreams.Count    > 0;
    public bool HasAudioStreams    => MediaInfo?.AudioStreams.Count    > 0;
    public bool HasSubtitleStreams => MediaInfo?.SubtitleStreams.Count > 0;

    // ────────────────────────────────────────────────────────────────

    public VideoAnalyzerViewModel(
        IMediaFileReader reader,
        ILogger<VideoAnalyzerViewModel> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "미디어 파일 선택",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts;*.mp3;*.aac;*.wav;*.flac;*.ogg|모든 파일|*.*",
        };
        if (dlg.ShowDialog() == true)
            FilePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        IsAnalyzing = true;
        StatusText  = "분석 중…";
        MediaInfo   = null;
        HasResult   = false;

        try
        {
            MediaInfo = await _reader.ReadAsync(FilePath);
            HasResult  = true;
            StatusText = $"분석 완료: {MediaInfo.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
            _logger.LogError(ex, "미디어 분석 실패: {Path}", FilePath);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(FilePath);

    [RelayCommand(CanExecute = nameof(CanCopyReport))]
    private void CopyReport()
    {
        if (MediaInfo is null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"파일명    : {MediaInfo.FileName}");
        sb.AppendLine($"경로      : {MediaInfo.FilePath}");
        sb.AppendLine($"크기      : {MediaInfo.FileSizeText}");
        sb.AppendLine($"포맷      : {MediaInfo.FormatLongName} ({MediaInfo.FormatName})");
        sb.AppendLine($"길이      : {MediaInfo.DurationText}");
        if (MediaInfo.BitRateText != null)
            sb.AppendLine($"비트레이트: {MediaInfo.BitRateText}");

        foreach (var v in MediaInfo.VideoStreams)
        {
            sb.Append($"[Video #{v.StreamIndex}] {v.CodecName} | {v.ResolutionText} | {v.FrameRateText}");
            if (v.BitRateMbps.HasValue) sb.Append($" | {v.BitRateMbps:F2} Mbps");
            sb.Append($" | {v.PixelFormat}");
            if (v.Profile != null) sb.Append($" | {v.Profile}");
            sb.AppendLine();
        }
        foreach (var a in MediaInfo.AudioStreams)
        {
            sb.Append($"[Audio #{a.StreamIndex}] {a.CodecName} | {a.SampleRateText} | {a.ChannelLayout}");
            if (a.BitRateText != null) sb.Append($" | {a.BitRateText}");
            if (a.Language    != null) sb.Append($" | {a.Language}");
            sb.AppendLine();
        }
        foreach (var s in MediaInfo.SubtitleStreams)
            sb.AppendLine($"[Subtitle #{s.StreamIndex}] {s.CodecName} | {s.DisplayLabel}");

        Clipboard.SetText(sb.ToString());
        StatusText = "클립보드에 복사됨";
    }

    private bool CanCopyReport() => !IsAnalyzing && HasResult;

    // ── 외부 진입점 (Drag-drop / 연동) ──────────────────────────────

    public async Task AnalyzeFileAsync(string path)
    {
        FilePath = path;
        await AnalyzeAsync();
    }
}
