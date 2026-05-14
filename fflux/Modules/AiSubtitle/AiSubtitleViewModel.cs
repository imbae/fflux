using fflux.AiSubtitle.Services.Subtitle;
using fflux.AiSubtitle.Services.Translation;
using fflux.AiSubtitle.Models;
using Microsoft.Win32;

namespace fflux.UI.Modules.AiSubtitle;

/// <summary>
/// AI 자막 생성 페이지 ViewModel.
/// 동영상 파일 → Whisper 전사 → 배치 번역 → .srt 파일 저장 파이프라인을 UI에 연결합니다.
/// </summary>
public sealed partial class AiSubtitleViewModel : ObservableObject
{
    private readonly ISubtitleGenerationService _generationService;
    private readonly ILogger<AiSubtitleViewModel> _logger;

    private CancellationTokenSource? _generateCts;

    // ── 바인딩 프로퍼티 ──────────────────────────────────────────────

    /// <summary>선택된 동영상 파일 경로.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _mediaFilePath = string.Empty;

    /// <summary>저장할 .srt 경로 (비어있으면 자동 결정).</summary>
    [ObservableProperty]
    private string _outputSrtPath = string.Empty;

    /// <summary>번역 대상 언어 코드 (예: "ko", "en").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _targetLanguage = "ko";

    /// <summary>번역 엔진 인덱스 (0=Groq, 1=DeepL).</summary>
    [ObservableProperty]
    private int _engineIndex;

    /// <summary>생성 중 진행률 표시 (0.0~1.0).</summary>
    [ObservableProperty]
    private double _overallProgress;

    /// <summary>현재 단계 텍스트 (예: "전사 중…").</summary>
    [ObservableProperty]
    private string _stageText = string.Empty;

    /// <summary>생성 중 여부 — 생성 버튼 비활성화에 사용.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isGenerating;

    /// <summary>결과 메시지 (성공/실패).</summary>
    [ObservableProperty]
    private string _resultMessage = string.Empty;

    /// <summary>결과 메시지 색상.</summary>
    [ObservableProperty]
    private Brush _resultColor = Brushes.Gray;

    // ── 지원 언어 목록 ───────────────────────────────────────────────

    public static IReadOnlyList<LanguageItem> SupportedLanguages { get; } =
    [
        new("한국어",   "ko"),
        new("English", "en"),
        new("日本語",   "ja"),
        new("中文",     "zh"),
        new("Español", "es"),
        new("Français","fr"),
        new("Deutsch", "de"),
    ];

    public static IReadOnlyList<string> EngineNames { get; } =
    [
        "Groq (Llama 4)",
        "DeepL",
    ];

    // ── 생성자 ──────────────────────────────────────────────────────

    public AiSubtitleViewModel(
        ISubtitleGenerationService generationService,
        ILogger<AiSubtitleViewModel> logger)
    {
        _generationService = generationService;
        _logger = logger;
    }

    // ── 커맨드 ──────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseMediaFile()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "동영상 파일 선택",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        MediaFilePath = dlg.FileName;
        // 출력 경로를 비우면 자동 결정 (동일 폴더, 동일 파일명.srt)
        OutputSrtPath = string.Empty;
    }

    [RelayCommand]
    private void BrowseOutputPath()
    {
        var saveDialog = new SaveFileDialog
        {
            Title      = "저장할 .srt 경로 선택",
            Filter     = "SRT 자막|*.srt",
            FileName   = string.IsNullOrWhiteSpace(MediaFilePath)
                ? "subtitle.srt"
                : Path.ChangeExtension(Path.GetFileName(MediaFilePath), ".srt"),
            InitialDirectory = string.IsNullOrWhiteSpace(MediaFilePath)
                ? string.Empty
                : Path.GetDirectoryName(MediaFilePath) ?? string.Empty,
        };
        if (saveDialog.ShowDialog() != true) return;
        OutputSrtPath = saveDialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        IsGenerating = true;
        ResultMessage = string.Empty;
        OverallProgress = 0.0;

        _generateCts?.Dispose();
        _generateCts = new CancellationTokenSource();

        var engine = EngineIndex == 1 ? TranslationEngine.DeepL : TranslationEngine.Groq;

        var request = new SubtitleGenerationRequest(
            MediaFilePath:  MediaFilePath,
            TargetLanguage: TargetLanguage,
            OutputSrtPath:  OutputSrtPath,
            Engine:         engine);

        var progress = new Progress<SubtitleGenerationProgress>(OnProgress);

        try
        {
            string resultPath = await _generationService.GenerateAsync(
                request, progress, _generateCts.Token);

            ResultMessage = $"✅ 저장 완료: {Path.GetFileName(resultPath)}";
            ResultColor   = Brushes.LightGreen;
            OverallProgress = 1.0;
            _logger.LogInformation("자막 생성 완료: {Path}", resultPath);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "⛔ 취소되었습니다.";
            ResultColor   = Brushes.Orange;
            OverallProgress = 0.0;
        }
        catch (Exception ex)
        {
            ResultMessage = $"❌ 오류: {ex.Message}";
            ResultColor   = Brushes.Red;
            _logger.LogError(ex, "자막 생성 실패: {File}", MediaFilePath);
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _generateCts?.Cancel();
    }

    private bool CanGenerate() =>
        !IsGenerating
        && !string.IsNullOrWhiteSpace(MediaFilePath)
        && !string.IsNullOrWhiteSpace(TargetLanguage);

    private bool CanCancel() => IsGenerating;

    // ── 진행률 처리 ─────────────────────────────────────────────────

    private void OnProgress(SubtitleGenerationProgress p)
    {
        // 전체 진행률: 전사(0~0.5) + 번역(0.5~0.95) + 저장(0.95~1.0)
        double baseProgress = p.Stage switch
        {
            SubtitleGenerationStage.Transcribing => 0.0 + p.StageProgress * 0.5,
            SubtitleGenerationStage.Translating  => 0.5 + p.StageProgress * 0.45,
            SubtitleGenerationStage.Saving       => 0.95 + p.StageProgress * 0.05,
            _                                    => OverallProgress,
        };

        StageText = p.Stage switch
        {
            SubtitleGenerationStage.Transcribing => $"🎙 전사 중… {p.StageProgress:P0}",
            SubtitleGenerationStage.Translating  => $"🌐 번역 중… {p.StageProgress:P0}",
            SubtitleGenerationStage.Saving       => "💾 저장 중…",
            _                                    => StageText,
        };

        OverallProgress = baseProgress;
    }
}

/// <summary>언어 표시명 + 코드 쌍.</summary>
public sealed record LanguageItem(string DisplayName, string Code);
