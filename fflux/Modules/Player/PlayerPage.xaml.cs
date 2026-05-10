using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace fflux.UI.Modules.Player;

public partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }

    // ── 자동숨김 타이머 ───────────────────────────────────────────────
    // 재생 중 3초간 마우스 움직임이 없으면 컨트롤 바를 페이드아웃합니다.

    private readonly DispatcherTimer _hideTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3),
    };

    // ── 풀스크린 상태 ─────────────────────────────────────────────────

    private WindowStyle _savedStyle;
    private WindowState _savedState;
    private bool        _fullscreenApplied;

    // ── 생성자 ──────────────────────────────────────────────────────

    public PlayerPage(PlayerViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── 초기화 ──────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Focus();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (ViewModel.IsPlaying)
                SetControlBarVisible(false);
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.IsPlaying):
                if (!ViewModel.IsPlaying)
                {
                    _hideTimer.Stop();
                    SetControlBarVisible(true);
                }
                break;

            case nameof(PlayerViewModel.IsFullscreen):
                ApplyFullscreen(ViewModel.IsFullscreen);
                break;
        }
    }

    // ── DragDrop ─────────────────────────────────────────────────────

    private void OnVideoDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnVideoDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var file = files[0];
        var ext  = Path.GetExtension(file).ToLowerInvariant();

        // 자막 파일이면 자막 로드, 그 외에는 미디어 파일로 열기
        if (ext is ".srt" or ".vtt")
            _ = ViewModel.LoadSubtitleFromPathAsync(file);
        else
            _ = ViewModel.OpenDroppedFileAsync(file);
    }

    // ── 마우스 이벤트 (자동숨김 트리거) ──────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        SetControlBarVisible(true);

        if (ViewModel.IsPlaying)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        SetControlBarVisible(true);
    }

    // ── 컨트롤 바 페이드 애니메이션 ─────────────────────────────────

    private void SetControlBarVisible(bool visible)
    {
        double target = visible ? 1.0 : 0.0;
        if (Math.Abs(ControlBar.Opacity - target) < 0.01) return;

        ControlBar.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(target,
                TimeSpan.FromMilliseconds(visible ? 150 : 450)));

        Mouse.OverrideCursor = visible ? null : Cursors.None;
    }

    // ── 풀스크린 전환 ────────────────────────────────────────────────

    private void ApplyFullscreen(bool fullscreen)
    {
        var window = Window.GetWindow(this);
        if (window == null) return;

        if (fullscreen && !_fullscreenApplied)
        {
            _savedStyle = window.WindowStyle;
            _savedState = window.WindowState;
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Maximized;
            _fullscreenApplied = true;
        }
        else if (!fullscreen && _fullscreenApplied)
        {
            window.WindowStyle = _savedStyle;
            window.WindowState = _savedState;
            _fullscreenApplied = false;
        }
    }

    // ── 키보드 단축키 ────────────────────────────────────────────────
    //
    //  Space       재생 / 일시정지 토글
    //  ←           5초 뒤로
    //  →           5초 앞으로
    //  Ctrl+←      이전 프레임
    //  Ctrl+→      다음 프레임
    //  M           음소거 토글
    //  F           풀스크린 토글
    //  Escape      풀스크린 해제

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Slider가 포커스를 가질 때는 Slider 자체가 ← → 를 처리하므로 스킵
        if (e.OriginalSource is Slider) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Space:
                if (ViewModel.IsPlaying)
                    ViewModel.PauseCommand.Execute(null);
                else if (ViewModel.IsFileOpen)
                    ViewModel.PlayCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
                if (ctrl) ViewModel.StepFrameBackwardCommand.Execute(null);
                else       _ = ViewModel.SeekRelativeAsync(-5.0);
                e.Handled = true;
                break;

            case Key.Right:
                if (ctrl) ViewModel.StepFrameForwardCommand.Execute(null);
                else       _ = ViewModel.SeekRelativeAsync(+5.0);
                e.Handled = true;
                break;

            case Key.M:
                ViewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F:
                ViewModel.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.V:
                ViewModel.ToggleSubtitleCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when ViewModel.IsFullscreen:
                ViewModel.IsFullscreen = false;
                e.Handled = true;
                break;
        }
    }
}
