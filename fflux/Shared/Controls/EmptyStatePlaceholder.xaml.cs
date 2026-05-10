using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace fflux.UI.Shared.Controls;

/// <summary>
/// 파일이 없거나 데이터가 없을 때 표시하는 빈 상태 안내 컨트롤.
/// </summary>
public partial class EmptyStatePlaceholder : UserControl
{
    // ── Symbol ──────────────────────────────────────────────
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(
            nameof(Symbol), typeof(SymbolRegular), typeof(EmptyStatePlaceholder),
            new PropertyMetadata(SymbolRegular.DocumentSearch24));

    /// <summary>표시할 Fluent UI 아이콘 심볼</summary>
    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    // ── Title ────────────────────────────────────────────────
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(EmptyStatePlaceholder),
            new PropertyMetadata(string.Empty));

    /// <summary>안내 제목 텍스트</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    // ── Description ─────────────────────────────────────────
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description), typeof(string), typeof(EmptyStatePlaceholder),
            new PropertyMetadata(string.Empty));

    /// <summary>부가 설명 텍스트</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    // ── ActionText ───────────────────────────────────────────
    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(
            nameof(ActionText), typeof(string), typeof(EmptyStatePlaceholder),
            new PropertyMetadata(string.Empty));

    /// <summary>액션 버튼 텍스트. 빈 문자열이면 버튼 숨김.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    // ── ActionCommand ────────────────────────────────────────
    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(
            nameof(ActionCommand), typeof(ICommand), typeof(EmptyStatePlaceholder),
            new PropertyMetadata(null));

    /// <summary>액션 버튼 클릭 시 실행할 커맨드</summary>
    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public EmptyStatePlaceholder()
    {
        InitializeComponent();
    }
}
