using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using fflux.Core.Abstractions;
using fflux.Core.Models;
using fflux.UI.Modules.Player;
using fflux.UI.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace fflux.UI.Modules.SubtitleEditor;

/// <summary>
/// 자막 편집기 페이지 ViewModel.
/// <para>
/// 기능:
/// <list type="bullet">
///   <item>SRT / VTT 파일 열기·저장 (SRT 형식으로 저장)</item>
///   <item>DataGrid 기반 타임스탬프·텍스트 인라인 편집</item>
///   <item>자막 추가·삭제</item>
///   <item>재생 연동: 재생 위치 → 현재 큐 하이라이트, 자막 위치로 플레이어 이동</item>
///   <item>재생 위치로 타임스탬프 설정 (Set Start/End from Player)</item>
///   <item>내장 플레이어 (PlayerVm) — 비디오 미리보기 + 자막 오버레이</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class SubtitleEditorViewModel : ObservableObject, IDisposable
{
    private static LocalizationManager Loc => LocalizationManager.Instance;
    // 비디오 파일 자동 연결 시 검색할 확장자 (우선순위 순)
    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".ts", ".m2ts", ".m4v", ".flv"];

    private readonly IServiceProvider _services;

    // ── 플레이어 ─────────────────────────────────────────────────────

    /// <summary>내장 비디오 플레이어 ViewModel (XAML 바인딩용으로 공개).</summary>
    public PlayerViewModel PlayerVm { get; }

    // 현재 재생 중인 큐 (플레이어 위치 기반 하이라이트)
    private SubtitleCueViewModel? _activeCue;

    // ── Observable 컬렉션 ────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<SubtitleCueViewModel> _cues = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCueCommand))]
    [NotifyCanExecuteChangedFor(nameof(SeekToSelectedCueCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetStartFromPlayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetEndFromPlayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCueBelowCommand))]
    private SubtitleCueViewModel? _selectedCue;

    // ── 상태 ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFileCommand))]
    private bool _isModified;

    [ObservableProperty]
    private bool _hasCues;

    // ── 자막 오버레이 (내장 비디오 화면에 표시) ─────────────────────

    /// <summary>현재 재생 위치의 자막 텍스트 (비디오 오버레이 바인딩).</summary>
    [ObservableProperty]
    private string _activeCueText = string.Empty;

    /// <summary>현재 재생 위치의 자막 색상 (비디오 오버레이 바인딩).</summary>
    [ObservableProperty]
    private Brush _activeCueBrush = Brushes.White;

    // ── UI → 코드비하인드 이벤트 (스크롤 등 순수 View 동작) ─────────
    public event EventHandler<SubtitleCueViewModel>? RequestScrollToCue;

    // ── 드래그앤드롭 진입점 (코드비하인드 대신 ViewModel에서 처리) ────

    private static readonly HashSet<string> _videoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".m2ts", ".m4v" };

    private static readonly HashSet<string> _subtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt", ".vtt" };

    /// <summary>
    /// 드래그앤드롭으로 파일을 받았을 때 호출합니다.
    /// 비디오 파일이면 내장 플레이어에 로드하고, 자막 파일이면 편집기에 로드합니다.
    /// 비디오를 열 때 현재 편집 중인 자막의 오버레이 상태도 초기화됩니다.
    /// </summary>
    public async Task DropFileAsync(string path)
    {
        var ext = Path.GetExtension(path);

        if (_videoExtensions.Contains(ext))
        {
            // 비디오 파일: 기존 오버레이 상태 초기화 후 플레이어에 로드
            _activeCue      = null;
            ActiveCueText   = string.Empty;
            ActiveCueBrush  = Brushes.White;
            await PlayerVm.OpenDroppedFileAsync(path);
        }
        else if (_subtitleExtensions.Contains(ext))
        {
            await LoadFromPathAsync(path);
        }
    }

    /// <summary>
    /// 내장 플레이어의 재생 / 일시정지를 토글합니다.
    /// Space 키 단축키에서 호출합니다.
    /// </summary>
    public void TogglePlayPause()
    {
        if (PlayerVm.IsPlaying)
        {
            if (PlayerVm.PauseCommand.CanExecute(null))
                PlayerVm.PauseCommand.Execute(null);
        }
        else
        {
            if (PlayerVm.PlayCommand.CanExecute(null))
                PlayerVm.PlayCommand.Execute(null);
        }
    }

    // ────────────────────────────────────────────────────────────────

    public SubtitleEditorViewModel(IServiceProvider services, PlayerViewModel playerVm)
    {
        _services = services;
        PlayerVm  = playerVm;

        StatusText = Loc["SubtitleEditor.Empty.Title"];

        PlayerVm.PositionChanged += OnPlayerPositionChanged;
        Cues.CollectionChanged   += OnCuesCollectionChanged;
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!HasCues && FilePath is null)
            StatusText = Loc["SubtitleEditor.Empty.Title"];
        else
            RefreshStatus();
    }

    // ═══════════════════════════════════════════════════════════════
    // 파일 열기 / 저장
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title  = Loc["SubtitleEditor.Dialog.Open"],
            Filter = Loc["SubtitleEditor.Dialog.OpenFilter"],
        };
        if (dlg.ShowDialog() != true) return;

        await LoadFromPathAsync(dlg.FileName);
    }

    /// <summary>코드비하인드의 DragDrop 핸들러 및 명령에서 공통으로 사용합니다.</summary>
    public async Task LoadFromPathAsync(string path)
    {
        var ext    = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        var parser = _services.GetKeyedService<ISubtitleParser>(ext);

        if (parser is null)
        {
            StatusText = string.Format(Loc["SubtitleEditor.Status.Unsupported"], ext);
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path);
            var entries = parser.Parse(content);

            LoadEntries(entries, path);

            // 같은 디렉터리에서 동일 이름의 비디오 파일 자동 연결 (재생은 하지 않음)
            await TryAutoOpenVideoAsync(path);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Loc["SubtitleEditor.Status.OpenFailed"], ex.Message);
        }
    }

    /// <summary>
    /// 자막 파일과 같은 디렉터리에 동일한 기본 이름의 비디오 파일이 있으면
    /// PlayerVm에 재생 없이 로드합니다.
    /// </summary>
    private async Task TryAutoOpenVideoAsync(string subtitlePath)
    {
        var dir  = Path.GetDirectoryName(subtitlePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(subtitlePath);

        string? videoPath = null;
        foreach (var ext in VideoExtensions)
        {
            var candidate = Path.Combine(dir, stem + ext);
            if (File.Exists(candidate)) { videoPath = candidate; break; }
        }

        if (videoPath is null) return;

        try
        {
            await PlayerVm.OpenFileWithoutPlayAsync(videoPath);
        }
        catch
        {
            // 비디오 열기 실패는 자막 편집에 영향 없음 — 무시
        }
    }

    /// <summary>항목 목록을 Cues 컬렉션에 로드합니다.</summary>
    public void LoadEntries(IReadOnlyList<SubtitleEntry> entries, string? path = null)
    {
        // 기존 큐 구독 해제
        foreach (var old in Cues)
            old.PropertyChanged -= OnCuePropertyChanged;

        Cues.Clear();

        int idx = 1;
        foreach (var e in entries)
        {
            var vm = SubtitleCueViewModel.FromEntry(idx++, e);
            vm.PropertyChanged += OnCuePropertyChanged;
            Cues.Add(vm);
        }

        if (path != null)
            FilePath = path;

        IsModified      = false;
        _activeCue      = null;
        ActiveCueText   = string.Empty;
        ActiveCueBrush  = Brushes.White;
        RefreshStatus();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveFileAsync()
    {
        if (FilePath is null)
        {
            await SaveFileAsAsync();
            return;
        }

        // VTT 파일은 SRT로만 저장 → Save As 다이얼로그 표시
        if (Path.GetExtension(FilePath).Equals(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            await SaveFileAsAsync();
            return;
        }

        await WriteToPathAsync(FilePath);
    }

    private bool CanSave() => IsModified;

    [RelayCommand]
    private async Task SaveFileAsAsync()
    {
        var baseName = FilePath is not null
            ? Path.GetFileNameWithoutExtension(FilePath)
            : "subtitle";

        var dlg = new SaveFileDialog
        {
            Title      = Loc["SubtitleEditor.Dialog.SaveAs"],
            Filter     = "SubRip|*.srt",
            FileName   = baseName + ".srt",
            DefaultExt = ".srt",
        };
        if (dlg.ShowDialog() != true) return;

        await WriteToPathAsync(dlg.FileName);
        FilePath = dlg.FileName;
    }

    private async Task WriteToPathAsync(string path)
    {
        var entries = Cues
            .Select(c => c.ToEntry())
            .Where(e => e is not null)
            .Cast<SubtitleEntry>()
            .OrderBy(e => e.Start)
            .ToList();

        bool hasErrors = Cues.Any(c => c.HasError);
        var serializer = _services.GetRequiredService<ISubtitleSerializer>();
        var content    = serializer.Serialize(entries);

        await File.WriteAllTextAsync(path, content, System.Text.Encoding.UTF8);

        IsModified = false;
        RefreshStatus();

        if (hasErrors)
            StatusText += "  " + Loc["SubtitleEditor.Status.SaveWarning"];
    }

    // ═══════════════════════════════════════════════════════════════
    // 편집 커맨드
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanAddCueBelow))]
    private void AddCueBelow()
    {
        // 삽입 위치: 선택된 큐 다음, 없으면 맨 끝
        int insertAt = SelectedCue is not null
            ? Cues.IndexOf(SelectedCue) + 1
            : Cues.Count;

        // 기본 시작 시간: 이전 큐의 종료 시간
        var prevEnd = insertAt > 0
            ? (Cues[insertAt - 1].End ?? TimeSpan.Zero)
            : TimeSpan.Zero;

        var vm = new SubtitleCueViewModel
        {
            StartText = SubtitleCueViewModel.FormatTimestamp(prevEnd),
            EndText   = SubtitleCueViewModel.FormatTimestamp(prevEnd + TimeSpan.FromSeconds(2)),
            Text      = "",
        };
        vm.PropertyChanged += OnCuePropertyChanged;

        Cues.Insert(insertAt, vm);
        RenumberCues();
        SelectedCue = vm;
        IsModified  = true;
        RefreshStatus();
    }

    private bool CanAddCueBelow() => true; // 항상 추가 가능

    [RelayCommand(CanExecute = nameof(CanDeleteCue))]
    private void DeleteCue()
    {
        if (SelectedCue is null) return;

        int idx = Cues.IndexOf(SelectedCue);
        SelectedCue.PropertyChanged -= OnCuePropertyChanged;

        // 삭제 대상이 현재 활성 큐이면 IsActive 플래그와 오버레이 상태를 즉시 초기화합니다.
        if (ReferenceEquals(_activeCue, SelectedCue))
        {
            _activeCue.IsActive = false;
            _activeCue          = null;
            ActiveCueText       = string.Empty;
            ActiveCueBrush      = Brushes.White;
        }

        Cues.Remove(SelectedCue);
        RenumberCues();

        // 인접한 큐 자동 선택
        SelectedCue = Cues.Count > 0
            ? Cues[Math.Min(idx, Cues.Count - 1)]
            : null;

        IsModified = true;
        RefreshStatus();
    }

    private bool CanDeleteCue() => SelectedCue is not null;

    // ═══════════════════════════════════════════════════════════════
    // 재생 연동
    // ═══════════════════════════════════════════════════════════════

    /// <summary>선택된 큐의 시작 시간으로 플레이어를 이동합니다.</summary>
    [RelayCommand(CanExecute = nameof(CanSeekToSelectedCue))]
    private async Task SeekToSelectedCueAsync()
    {
        if (SelectedCue?.Start is not { } start) return;
        // SeekRelativeAsync는 내부에서 PositionSeconds를 다시 읽어 이중 읽기 경쟁이 발생합니다.
        // SeekToAsync는 목표 위치를 직접 받으므로 재생 중에도 정확한 위치로 이동합니다.
        await PlayerVm.SeekToAsync(start);
    }

    private bool CanSeekToSelectedCue() => SelectedCue?.Start is not null;

    /// <summary>현재 플레이어 위치를 선택된 큐의 시작 시간으로 설정합니다.</summary>
    [RelayCommand(CanExecute = nameof(CanSetFromPlayer))]
    private void SetStartFromPlayer()
    {
        if (SelectedCue is null) return;
        var pos = TimeSpan.FromSeconds(PlayerVm.PositionSeconds);
        SelectedCue.StartText = SubtitleCueViewModel.FormatTimestamp(pos);
    }

    /// <summary>현재 플레이어 위치를 선택된 큐의 종료 시간으로 설정합니다.</summary>
    [RelayCommand(CanExecute = nameof(CanSetFromPlayer))]
    private void SetEndFromPlayer()
    {
        if (SelectedCue is null) return;
        var pos = TimeSpan.FromSeconds(PlayerVm.PositionSeconds);
        SelectedCue.EndText = SubtitleCueViewModel.FormatTimestamp(pos);
    }

    private bool CanSetFromPlayer() => SelectedCue is not null;

    // ═══════════════════════════════════════════════════════════════
    // 플레이어 위치 변경 → 현재 큐 하이라이트 + 오버레이 업데이트
    // (PlayerViewModel.UpdateTimecode → UI 스레드에서 발화)
    // ═══════════════════════════════════════════════════════════════

    private void OnPlayerPositionChanged(object? sender, TimeSpan position)
    {
        if (Cues.Count == 0)
        {
            if (_activeCue is not null)
            {
                _activeCue.IsActive = false;
                _activeCue = null;
                ActiveCueText  = string.Empty;
                ActiveCueBrush = Brushes.White;
            }
            return;
        }

        var newActive = FindActiveCue(position);

        if (ReferenceEquals(newActive, _activeCue)) return;

        if (_activeCue is not null) _activeCue.IsActive = false;
        _activeCue = newActive;

        if (newActive is not null)
        {
            newActive.IsActive = true;
            ActiveCueText      = newActive.Text;
            ActiveCueBrush     = newActive.ColorBrush;
            RequestScrollToCue?.Invoke(this, newActive);
        }
        else
        {
            ActiveCueText  = string.Empty;
            ActiveCueBrush = Brushes.White;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 헬퍼
    // ═══════════════════════════════════════════════════════════════

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IsActive, Index 변경은 내용 수정이 아님
        if (e.PropertyName is nameof(SubtitleCueViewModel.IsActive)
                           or nameof(SubtitleCueViewModel.Index))
            return;

        IsModified = true;
        RefreshStatus();

        // 활성 큐의 색상이 바뀌면 오버레이도 즉시 반영
        if (sender is SubtitleCueViewModel cue
            && ReferenceEquals(cue, _activeCue)
            && e.PropertyName is nameof(SubtitleCueViewModel.ColorBrush)
                               or nameof(SubtitleCueViewModel.Text))
        {
            ActiveCueText  = cue.Text;
            ActiveCueBrush = cue.ColorBrush;
        }
    }

    private void OnCuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasCues = Cues.Count > 0;
        AddCueBelowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// <paramref name="position"/>에 해당하는 큐를 이진 탐색으로 찾습니다.
    /// <para>
    /// Cues는 파일 로드 시 <c>Start</c> 기준 오름차순으로 정렬됩니다.
    /// 사용자가 타임스탬프를 편집하면 순서가 흐트러질 수 있으므로,
    /// 이진 탐색 실패 시 선형 탐색으로 폴백합니다.
    /// </para>
    /// </summary>
    private SubtitleCueViewModel? FindActiveCue(TimeSpan position)
    {
        // 이진 탐색: Start ≤ position < End 인 큐를 찾습니다.
        int lo = 0, hi = Cues.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var cue = Cues[mid];
            var s   = cue.Start;
            var e   = cue.End;

            if (s is null || e is null)
            {
                // 타임스탬프 오류 큐가 있으면 이진 탐색 불가 — 선형으로 폴백
                goto LinearFallback;
            }

            if (e.Value <= position)
                lo = mid + 1;
            else if (s.Value > position)
                hi = mid - 1;
            else
                return cue; // Start ≤ position < End
        }
        return null;

    LinearFallback:
        foreach (var cue in Cues)
            if (cue.Start is { } s && cue.End is { } e && position >= s && position < e)
                return cue;
        return null;
    }

    private void RenumberCues()
    {
        for (int i = 0; i < Cues.Count; i++)
            Cues[i].Index = i + 1;
    }

    private void RefreshStatus()
    {
        var name = FilePath is not null ? Path.GetFileName(FilePath) : Loc["SubtitleEditor.Status.Unsaved"];
        var mod  = IsModified ? Loc["SubtitleEditor.Status.Modified"] : "";
        StatusText = $"{name} • {Cues.Count} {Loc["SubtitleEditor.Status.Cues"]}{mod}";
    }

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        PlayerVm.PositionChanged -= OnPlayerPositionChanged;
        Cues.CollectionChanged   -= OnCuesCollectionChanged;
        LocalizationManager.Instance.PropertyChanged -= OnLocalizationChanged;

        foreach (var cue in Cues)
            cue.PropertyChanged -= OnCuePropertyChanged;
    }
}
