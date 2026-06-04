using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace fflux.UI.Shared.Controls;

/// <summary>
/// 작업 진행 중 화면 전체를 덮는 오버레이 컨트롤.
/// 상위 Grid에 Grid.RowSpan 등으로 겹치게 배치한 뒤 IsActive로 표시/숨김.
/// </summary>
public partial class LoadingOverlay : UserControl
{
    // ── IsActive ────────────────────────────────────────────
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(LoadingOverlay),
            new PropertyMetadata(false));

    /// <summary>오버레이 표시 여부</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    // ── Message ─────────────────────────────────────────────
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(LoadingOverlay),
            new PropertyMetadata("처리 중..."));

    /// <summary>진행 상태 메시지</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    // ── Progress ────────────────────────────────────────────
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress), typeof(double), typeof(LoadingOverlay),
            new PropertyMetadata(0.0));

    /// <summary>진행률 (0~100). IsIndeterminate=true 이면 무시됨.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    // ── IsIndeterminate ─────────────────────────────────────
    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(
            nameof(IsIndeterminate), typeof(bool), typeof(LoadingOverlay),
            new PropertyMetadata(true));

    /// <summary>true이면 무한 로딩 애니메이션, false이면 Progress 값 사용</summary>
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    // ── CanCancel ────────────────────────────────────────────
    public static readonly DependencyProperty CanCancelProperty =
        DependencyProperty.Register(
            nameof(CanCancel), typeof(bool), typeof(LoadingOverlay),
            new PropertyMetadata(false));

    /// <summary>true이면 취소 버튼 표시</summary>
    public bool CanCancel
    {
        get => (bool)GetValue(CanCancelProperty);
        set => SetValue(CanCancelProperty, value);
    }

    // ── CancelCommand ────────────────────────────────────────
    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(
            nameof(CancelCommand), typeof(ICommand), typeof(LoadingOverlay),
            new PropertyMetadata(null));

    /// <summary>취소 버튼 클릭 시 실행되는 커맨드</summary>
    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public LoadingOverlay()
    {
        InitializeComponent();
    }
}
