#if AI_SUBTITLE
using System.Collections.ObjectModel;
using System.Net.Http;
using fflux.AiSubtitle.DependencyInjection;
using fflux.AiSubtitle.Services.Subtitle;
using fflux.AiSubtitle.Services.Translation;
using fflux.AiSubtitle.Models;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace fflux.UI.Modules.AiSubtitle;

/// <summary>
/// AI 자막 생성 페이지 ViewModel.
/// 동영상 파일 → Whisper 전사 → 배치 번역 → .srt 파일 저장 파이프라인을 UI에 연결합니다.
/// </summary>
public sealed partial class AiSubtitleViewModel : ObservableObject, IDisposable
{
    private readonly ISubtitleGenerationService _generationService;
    private readonly AiSubtitleOptions _options;
    private readonly ILogger<AiSubtitleViewModel> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private CancellationTokenSource? _generateCts;
    private Process? _serverProcess;
    private SubtitleGenerationStage? _lastStage;

    // ── 서버 상태 ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
    private bool _isServerRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
    private bool _isStartingServer;

    [ObservableProperty] private string _serverStatusText = "○ 서버 미연결";

    /// <summary>현재 서버 URL이 로컬호스트인지 여부 (서버 시작 버튼 표시 조건).</summary>
    public bool IsLocalServer =>
        string.IsNullOrEmpty(_options.PythonApiUrl) ||
        _options.PythonApiUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        _options.PythonApiUrl.Contains("127.0.0.1", StringComparison.Ordinal);

    // ── Whisper 모델 선택 ────────────────────────────────────────────

    [ObservableProperty] private string _selectedWhisperModel = "base";
    [ObservableProperty] private bool _useGpu;

    public static IReadOnlyList<string> WhisperModels { get; } =
        ["tiny", "base", "small", "medium", "large-v3"];

    // ── 로그 ────────────────────────────────────────────────────────

    public ObservableCollection<string> Logs { get; } = [];

    // ── 생성 옵션 ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _mediaFilePath = string.Empty;

    [ObservableProperty] private string _outputSrtPath = string.Empty;
    [ObservableProperty] private string _sourceLanguage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _targetLanguage = "ko";

    [ObservableProperty] private int _engineIndex;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _stageText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isGenerating;

    [ObservableProperty] private string _resultMessage = string.Empty;
    [ObservableProperty] private Brush _resultColor = Brushes.Gray;

    [ObservableProperty]
    private TranslationStyle _translationStyle = TranslationStyle.Default;

    // ── 정적 목록 ────────────────────────────────────────────────────

    public static IReadOnlyList<LanguageItem> SupportedLanguages { get; } =
    [
        new("한국어", "ko"),
        new("English", "en"),
        new("日本語", "ja"),
    ];

    public static IReadOnlyList<LanguageItem> SourceLanguages { get; } =
    [
        new("자동 감지", ""),
        new("English",   "en"),
        new("日本語",     "ja"),
        new("한국어",     "ko"),
    ];

    public static IReadOnlyList<string> EngineNames { get; } =
    [
        "Groq (Llama 4)",
        "DeepL",
    ];

    public static IReadOnlyList<TranslationStyleInfo> TranslationStyles { get; } =
        TranslationStyleHelper.All;

    // ── 생성자 ──────────────────────────────────────────────────────

    public AiSubtitleViewModel(
        ISubtitleGenerationService generationService,
        IOptions<AiSubtitleOptions> options,
        ILogger<AiSubtitleViewModel> logger)
    {
        _generationService = generationService;
        _options = options.Value;
        _logger = logger;

        AddLog($"Python API URL: {(_options.PythonApiUrl is { Length: > 0 } u ? u : "(미설정)")}");
        AddLog("모델을 선택한 뒤 [서버 시작] 또는 [상태 확인]을 눌러주세요.");
    }

    /// <summary>
    /// modules/fflux.AiSubtitle.dll 이 없을 때 DI에 등록하는 비활성화 인스턴스를 생성합니다.
    /// AiSubtitlePage 네비 아이템이 숨겨지므로 이 ViewModel의 명령어는 호출되지 않습니다.
    /// </summary>
    internal static AiSubtitleViewModel CreateDisabled(ILogger<AiSubtitleViewModel> logger)
        => new(null!, Microsoft.Extensions.Options.Options.Create(new AiSubtitleOptions()), logger);

    // ══════════════════════════════════════════════════════════════════
    // 서버 관리 커맨드
    // ══════════════════════════════════════════════════════════════════

    /// <summary>서버 /health 엔드포인트를 호출해 상태를 확인합니다.</summary>
    [RelayCommand]
    private async Task CheckServerAsync()
    {
        var url = _options.PythonApiUrl;
        if (string.IsNullOrWhiteSpace(url)) { SetServerStatus(false, "⚠ PYTHON_API_URL 미설정"); return; }

        try
        {
            var resp = await _http.GetAsync($"{url.TrimEnd('/')}/health");
            if (resp.IsSuccessStatusCode)
            {
                SetServerStatus(true, "● 서버 연결됨");
                AddLog("서버 연결 확인 완료");
            }
            else
            {
                SetServerStatus(false, $"⚠ 서버 응답 오류 ({(int)resp.StatusCode})");
                AddLog($"서버 상태 확인 실패: HTTP {(int)resp.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            SetServerStatus(false, "○ 서버 미연결");
            AddLog($"서버 연결 실패: {ex.Message}");
        }
    }

    /// <summary>로컬 whisper_server.py 프로세스를 시작하고 준비될 때까지 대기합니다.</summary>
    [RelayCommand(CanExecute = nameof(CanStartServer))]
    private async Task StartServerAsync()
    {
        IsStartingServer = true;
        SetServerStatus(false, "◑ 서버 시작 중…");

        var scriptPath = FindServerScript();
        if (scriptPath is null)
        {
            AddLog("❌ whisper_server.py를 찾을 수 없습니다.");
            AddLog("   fflux.AiSubtitle/python/ 폴더를 확인하세요.");
            SetServerStatus(false, "⚠ 스크립트 없음");
            IsStartingServer = false;
            return;
        }

        AddLog($"스크립트 경로: {scriptPath}");
        AddLog($"모델: {SelectedWhisperModel}  디바이스: {(UseGpu ? "cuda" : "cpu")}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = BuildServerArgs(scriptPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // ── 기존 프로세스 완전 종료 대기 ─────────────────────────────
            // Kill() 은 신호를 보낼 뿐 즉시 종료되지 않습니다.
            // 종료를 기다리지 않으면 Python/uvicorn 이 포트를 점유한 채로
            // 새 프로세스가 같은 포트를 열려 해 WinSock 10048(WSAEADDRINUSE)이 발생합니다.
            if (_serverProcess is { HasExited: false })
            {
                AddLog("기존 서버 프로세스 종료 중…");
                try
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _serverProcess.WaitForExitAsync(killCts.Token);
                    AddLog("기존 프로세스 종료 완료.");
                }
                catch { /* 이미 종료됐거나 권한 없음 — 무시 */ }
            }
            _serverProcess?.Dispose();
            _serverProcess = null;

            // OS가 포트를 반환할 시간을 확보합니다.
            await Task.Delay(600);

            _serverProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _serverProcess.OutputDataReceived += (_, e) => { if (e.Data != null) AddLog(e.Data); };
            _serverProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) AddLog(e.Data); };
            _serverProcess.Exited += async (_, _) =>
            {
                // stderr/stdout 버퍼가 이벤트로 전달될 시간을 줍니다.
                await Task.Delay(400);

                var code = -1;
                try { code = _serverProcess?.ExitCode ?? -1; } catch { }

                AddLog($"⚠ 서버 프로세스 종료 (exit code: {code})");
                if (code != 0)
                {
                    AddLog("  → 위 로그의 Python 오류 메시지를 확인하세요.");
                    AddLog("  → 패키지 미설치 시: pip install -r requirements.txt");
                }
                SetServerStatus(false, "○ 서버 종료됨");
            };

            // Python 실행 가능 여부 및 버전 사전 확인
            if (!await CheckPythonAvailableAsync())
                return;

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            AddLog($"프로세스 시작 (PID {_serverProcess.Id}). 준비 대기 중…");

            // 최대 60초 폴링 — 프로세스 종료 시 즉시 탈출
            var ready = await WaitForServerReadyAsync(TimeSpan.FromSeconds(60));
            if (ready)
            {
                SetServerStatus(true, "● 서버 준비 완료");
                AddLog("✅ 서버가 준비되었습니다.");
            }
            else if (_serverProcess?.HasExited == true)
            {
                // Exited 핸들러에서 이미 처리됨
            }
            else
            {
                SetServerStatus(false, "⚠ 서버 시작 타임아웃");
                AddLog("❌ 60초 내에 서버가 응답하지 않았습니다.");
            }
        }
        catch (Exception ex)
        {
            SetServerStatus(false, "⚠ 시작 실패");
            AddLog($"❌ 서버 시작 오류: {ex.Message}");
        }
        finally
        {
            IsStartingServer = false;
        }
    }

    private bool CanStartServer() => !IsStartingServer && !IsServerRunning;

    // ══════════════════════════════════════════════════════════════════
    // 자막 생성 커맨드
    // ══════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void BrowseMediaFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "동영상 파일 선택",
            Filter = "미디어 파일|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m2ts|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        MediaFilePath = dlg.FileName;
        OutputSrtPath = string.Empty;
        AddLog($"파일 선택: {Path.GetFileName(dlg.FileName)}");
    }

    [RelayCommand]
    private void BrowseOutputPath()
    {
        var dlg = new SaveFileDialog
        {
            Title = "저장할 .srt 경로 선택",
            Filter = "SRT 자막|*.srt",
            FileName = string.IsNullOrWhiteSpace(MediaFilePath) ? "subtitle.srt"
                                   : Path.ChangeExtension(Path.GetFileName(MediaFilePath), ".srt"),
            InitialDirectory = string.IsNullOrWhiteSpace(MediaFilePath) ? string.Empty
                                   : Path.GetDirectoryName(MediaFilePath) ?? string.Empty,
        };
        if (dlg.ShowDialog() != true) return;
        OutputSrtPath = dlg.FileName;
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
            MediaFilePath: MediaFilePath,
            TargetLanguage: TargetLanguage,
            SourceLanguage: SourceLanguage,
            OutputSrtPath: OutputSrtPath,
            Engine: engine,
            Style: TranslationStyle);

        _lastStage = null;
        AddLog($"자막 생성 시작 — {Path.GetFileName(MediaFilePath)}");
        AddLog($"대상 언어: {TargetLanguage}  엔진: {(engine == TranslationEngine.DeepL ? "DeepL" : "Groq")}");

        var progress = new Progress<SubtitleGenerationProgress>(p =>
        {
            OnProgress(p);

            // 스테이지가 바뀔 때만 로그 출력
            // (서비스가 단계 내 중간값을 보고하지 않으므로 100%만 찍히는 문제 방지)
            if (p.Stage != _lastStage)
            {
                _lastStage = p.Stage;
                AddLog(p.Stage switch
                {
                    SubtitleGenerationStage.Transcribing => "🎙 전사 시작…",
                    SubtitleGenerationStage.Translating => "🌐 번역 시작…",
                    SubtitleGenerationStage.Saving => "💾 저장 중…",
                    _ => string.Empty,
                });
            }

            // 단계 완료(100%)일 때 완료 메시지
            if (p.StageProgress >= 1.0)
            {
                AddLog(p.Stage switch
                {
                    SubtitleGenerationStage.Transcribing => "🎙 전사 완료",
                    SubtitleGenerationStage.Translating => "🌐 번역 완료",
                    _ => string.Empty,
                });
            }
        });

        try
        {
            var path = await _generationService.GenerateAsync(request, progress, _generateCts.Token);
            ResultMessage = $"✅ 저장 완료: {Path.GetFileName(path)}";
            ResultColor = Brushes.LightGreen;
            OverallProgress = 1.0;
            AddLog($"✅ 완료: {path}");
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "⛔ 취소되었습니다.";
            ResultColor = Brushes.Orange;
            OverallProgress = 0.0;
            AddLog("⛔ 사용자가 취소했습니다.");
        }
        catch (Exception ex)
        {
            ResultMessage = $"❌ 오류: {ex.Message}";
            ResultColor = Brushes.Red;
            AddLog($"❌ 오류: {ex.Message}");
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
        AddLog("취소 요청…");
    }

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    private bool CanGenerate() =>
        !IsGenerating
        && IsServerRunning
        && !string.IsNullOrWhiteSpace(MediaFilePath)
        && !string.IsNullOrWhiteSpace(TargetLanguage);

    private bool CanCancel() => IsGenerating;

    // ══════════════════════════════════════════════════════════════════
    // 내부 헬퍼
    // ══════════════════════════════════════════════════════════════════

    private void SetServerStatus(bool running, string text)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsServerRunning = running;
            ServerStatusText = text;
        });
    }

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Application.Current.Dispatcher.InvokeAsync(() => Logs.Add(line));
    }

    private void OnProgress(SubtitleGenerationProgress p)
    {
        OverallProgress = p.Stage switch
        {
            SubtitleGenerationStage.Transcribing => p.StageProgress * 0.5,
            SubtitleGenerationStage.Translating => 0.5 + p.StageProgress * 0.45,
            SubtitleGenerationStage.Saving => 0.95 + p.StageProgress * 0.05,
            _ => OverallProgress,
        };
        StageText = p.Stage switch
        {
            SubtitleGenerationStage.Transcribing => $"🎙 전사 중… {p.StageProgress:P0}",
            SubtitleGenerationStage.Translating => $"🌐 번역 중… {p.StageProgress:P0}",
            SubtitleGenerationStage.Saving => "💾 저장 중…",
            _ => StageText,
        };
    }

    private async Task<bool> WaitForServerReadyAsync(TimeSpan timeout)
    {
        var url = _options.PythonApiUrl.TrimEnd('/') + "/health";
        var until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until)
        {
            // 프로세스가 이미 죽었으면 즉시 탈출
            if (_serverProcess?.HasExited == true)
            {
                AddLog("프로세스가 예기치 않게 종료되었습니다. 로그를 확인하세요.");
                return false;
            }

            try
            {
                var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* 아직 시작 중 */ }

            await Task.Delay(1500);
        }
        return false;
    }

    /// <summary>
    /// python 실행 가능 여부를 확인하고 버전을 로그에 기록합니다.
    /// 실행 불가 시 false 반환.
    /// </summary>
    private async Task<bool> CheckPythonAvailableAsync()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            proc.Start();

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var version = (stdout + stderr).Trim();
            if (proc.ExitCode == 0)
            {
                AddLog($"Python 확인: {version}");
                return true;
            }

            AddLog($"❌ python 실행 실패 (exit code: {proc.ExitCode}): {version}");
            SetServerStatus(false, "🔴 Python 실행 불가");
            return false;
        }
        catch (Exception ex)
        {
            AddLog($"❌ python을 찾을 수 없습니다: {ex.Message}");
            AddLog("  → Python이 PATH에 등록되어 있는지 확인하세요.");
            SetServerStatus(false, "🔴 Python 없음");
            return false;
        }
    }

    /// <summary>실행 파일에서 위로 탐색하여 whisper_server.py 경로를 찾습니다.</summary>
    private static string? FindServerScript()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "fflux.AiSubtitle", "python", "whisper_server.py");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar))!;
            if (string.IsNullOrEmpty(dir)) break;
        }
        return null;
    }

    private string BuildServerArgs(string scriptPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"\"{scriptPath}\"");
        sb.Append($" --model {SelectedWhisperModel}");
        sb.Append($" --device {(UseGpu ? "cuda" : "cpu")}");

        // URL에서 포트 추출
        if (Uri.TryCreate(_options.PythonApiUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
            sb.Append($" --port {uri.Port}");

        return sb.ToString();
    }

    public void Dispose()
    {
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _http.Dispose();

        if (_serverProcess is { HasExited: false })
        {
            try
            {
                _serverProcess.Kill(entireProcessTree: true);
                // 앱 종료 시 최대 3초 대기 (UI 블로킹 방지를 위해 짧게 유지)
                _serverProcess.WaitForExit(3000);
            }
            catch { }
        }
        _serverProcess?.Dispose();
        _serverProcess = null;
    }
}

/// <summary>언어 표시명 + 코드 쌍.</summary>
public sealed record LanguageItem(string DisplayName, string Code);

#else

// ── 스텁 (fflux.AiSubtitle 서브모듈 없을 때) ───────────────────────────────
using System.Collections.ObjectModel;

namespace fflux.UI.Modules.AiSubtitle;

public sealed record LanguageItem(string DisplayName, string Code);

public sealed partial class AiSubtitleViewModel : ObservableObject
{
    public static IReadOnlyList<LanguageItem> SupportedLanguages { get; } = [];
    public static IReadOnlyList<LanguageItem> SourceLanguages    { get; } = [];
    public static IReadOnlyList<string>        EngineNames        { get; } = [];
    public static IReadOnlyList<object>        TranslationStyles  { get; } = [];
    public static IReadOnlyList<string>        WhisperModels      { get; } = [];

    [ObservableProperty] private string _mediaFilePath   = string.Empty;
    [ObservableProperty] private string _outputSrtPath   = string.Empty;
    [ObservableProperty] private string _sourceLanguage  = string.Empty;
    [ObservableProperty] private string _targetLanguage  = string.Empty;
    [ObservableProperty] private int    _engineIndex;
    [ObservableProperty] private object? _translationStyle;
    [ObservableProperty] private string  _stageText       = string.Empty;
    [ObservableProperty] private double  _overallProgress;
    [ObservableProperty] private bool    _isGenerating;
    [ObservableProperty] private string  _resultMessage   = string.Empty;
    [ObservableProperty] private System.Windows.Media.Brush _resultColor =
        System.Windows.Media.Brushes.Gray;

    [ObservableProperty] private bool   _isServerRunning;
    [ObservableProperty] private bool   _isStartingServer;
    [ObservableProperty] private string _serverStatusText = "○ 서버 미연결";
    [ObservableProperty] private string _selectedWhisperModel = "base";
    [ObservableProperty] private bool   _useGpu;

    public bool IsLocalServer => false;
    public ObservableCollection<string> Logs { get; } = [];

    [RelayCommand] private void BrowseMediaFile()  { }
    [RelayCommand] private void BrowseOutputPath() { }
    [RelayCommand] private Task GenerateAsync()    => Task.CompletedTask;
    [RelayCommand] private void Cancel()           { }
    [RelayCommand] private Task CheckServerAsync() => Task.CompletedTask;
    [RelayCommand] private Task StartServerAsync() => Task.CompletedTask;
    [RelayCommand] private void ClearLogs()        { }
}

#endif
