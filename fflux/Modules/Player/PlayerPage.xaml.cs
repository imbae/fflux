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

        // ── 외부 스크롤바 원천 차단 ────────────────────────────────────────
        // WPF-UI NavigationView는 Frame을 ScrollViewer로 감싸고 있습니다.
        // 이 ScrollViewer의 기본 VerticalScrollBarVisibility=Auto 설정은
        // 자식(Frame → Page)에게 무한 높이(∞)를 Measure 단계에서 제공합니다.
        // 결과적으로 MisbMetadataPanel 내부의 Height="*" 행이 Auto처럼
        // 동작해 StackPanel 전체 높이가 DesiredSize로 보고되고
        // 페이지 레벨 스크롤바가 생기면서 하단 컨트롤바가 가려집니다.
        //
        // DisableAncestorScrollBars()는 이 Page → Window 사이의
        // 모든 ScrollViewer를 Disabled로 설정합니다.
        // Disabled 모드에서는 스크롤바가 숨겨지고, 뷰포트 크기(유한)만
        // 자식에게 전달되므로 Height="*" 행이 올바르게 동작합니다.
        DisableAncestorScrollBars();

        // DisableAncestorScrollBars로 인해 조상 ScrollViewer가 유한 공간을
        // 제공하게 되면, Page.ActualHeight = 뷰포트 높이가 됩니다.
        // 이후 창 크기 변경 시 SizeChanged가 올바른 값으로 트리거됩니다.
        ConstrainLayout(ActualWidth, ActualHeight);
        SizeChanged += OnPageSizeChanged;

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (ViewModel.IsPlaying)
                SetControlBarVisible(false);
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 이 Page에서 Window까지 시각적 부모 체인을 올라가며 발견되는 모든
    /// <see cref="ScrollViewer"/>의 수직·수평 스크롤바를
    /// <see cref="ScrollBarVisibility.Disabled"/>로 설정합니다.
    ///
    /// <para>
    /// <see cref="ScrollBarVisibility.Disabled"/>는 단순히 스크롤바 UI를 숨기는
    /// <see cref="ScrollBarVisibility.Hidden"/>과 달리, ScrollViewer가 자식에게
    /// 전달하는 availableSize를 뷰포트 크기(유한)로 제한합니다.
    /// 덕분에 내부 <c>Height="*"</c> 행이 올바르게 동작하고,
    /// MisbMetadataPanel 내부의 ScrollViewer만 스크롤바를 표시하게 됩니다.
    /// </para>
    /// </summary>
    private void DisableAncestorScrollBars()
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current is not null)
        {
            if (current is ScrollViewer sv)
            {
                sv.VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            if (current is Window) break;
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        => ConstrainLayout(e.NewSize.Width, e.NewSize.Height);

    /// <summary>
    /// RootGrid와 MisbMetadataPanel의 MaxWidth/MaxHeight를 뷰포트 크기로 제한합니다.
    /// DisableAncestorScrollBars와 이중 방어선을 구성합니다.
    /// </summary>
    private void ConstrainLayout(double width, double height)
    {
        if (width  > 0) RootGrid.MaxWidth  = width;
        if (height > 0) RootGrid.MaxHeight = height;

        // MisbMetadataPanel은 Margin="0,0,0,112"이므로
        // 패널 자체의 MaxHeight = 뷰포트 높이 - 하단 마진(112).
        if (height > 0) MisbPanel.MaxHeight = height - 112;
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
