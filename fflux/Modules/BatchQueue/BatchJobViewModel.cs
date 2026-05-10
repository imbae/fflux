using System.Text;

namespace fflux.UI.Modules.BatchQueue;

// ── 작업 상태 ──────────────────────────────────────────────────────────

public enum BatchJobStatus { Pending, Running, Done, Failed, Cancelled }

// ── 개별 인코딩 작업 ViewModel ──────────────────────────────────────────

public sealed partial class BatchJobViewModel : ObservableObject
{
    private readonly StringBuilder _logBuilder = new();

    // ── 파일 경로 ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string _inputFilePath;

    [ObservableProperty] private string _outputFilePath;

    // ── 상태 ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private BatchJobStatus _status = BatchJobStatus.Pending;

    [ObservableProperty] private double  _progress;
    [ObservableProperty] private string  _logText  = "";
    [ObservableProperty] private bool    _showLog;

    // ── 계산 프로퍼티 ──────────────────────────────────────────────

    public string FileName  => Path.GetFileName(InputFilePath);
    public bool   IsRunning => Status == BatchJobStatus.Running;
    public bool   IsFinished => Status is BatchJobStatus.Done
                                       or BatchJobStatus.Failed
                                       or BatchJobStatus.Cancelled;

    public string StatusLabel => Status switch
    {
        BatchJobStatus.Pending   => "대기 중",
        BatchJobStatus.Running   => "실행 중",
        BatchJobStatus.Done      => "완료",
        BatchJobStatus.Failed    => "실패",
        BatchJobStatus.Cancelled => "취소됨",
        _                        => "",
    };

    // ── 생성자 ─────────────────────────────────────────────────────

    public BatchJobViewModel(string inputFilePath, string outputFilePath)
    {
        _inputFilePath  = inputFilePath;
        _outputFilePath = outputFilePath;
    }

    // ── 메서드 ─────────────────────────────────────────────────────

    public void AppendLog(string line)
    {
        _logBuilder.AppendLine(line);
        LogText = _logBuilder.ToString();
    }

    /// <summary>작업을 대기 중 상태로 초기화합니다.</summary>
    public void Reset()
    {
        _logBuilder.Clear();
        LogText   = "";
        Progress  = 0;
        Status    = BatchJobStatus.Pending;
    }
}
