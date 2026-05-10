using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace fflux.UI.Shared.Controls;

/// <summary>
/// 비디오 위에 자막을 오버레이로 표시하는 컨트롤.
/// PlayerPage의 루트 Grid에 <c>VerticalAlignment="Bottom"</c>으로 배치하고
/// ViewModel의 프로퍼티를 바인딩하면 됩니다.
/// </summary>
public partial class SubtitleOverlayControl : UserControl
{
    // ── SubtitleText ─────────────────────────────────────────────────

    public static readonly DependencyProperty SubtitleTextProperty =
        DependencyProperty.Register(
            nameof(SubtitleText), typeof(string), typeof(SubtitleOverlayControl),
            new PropertyMetadata(string.Empty));

    /// <summary>현재 표시할 자막 텍스트. 비어 있으면 컨트롤이 자동으로 숨겨집니다.</summary>
    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    // ── SubtitleFontSize ─────────────────────────────────────────────

    public static readonly DependencyProperty SubtitleFontSizeProperty =
        DependencyProperty.Register(
            nameof(SubtitleFontSize), typeof(double), typeof(SubtitleOverlayControl),
            new PropertyMetadata(22.0));

    /// <summary>자막 텍스트 폰트 크기 (기본값: 22pt).</summary>
    public double SubtitleFontSize
    {
        get => (double)GetValue(SubtitleFontSizeProperty);
        set => SetValue(SubtitleFontSizeProperty, value);
    }

    // ── SubtitleColor ────────────────────────────────────────────────

    public static readonly DependencyProperty SubtitleColorProperty =
        DependencyProperty.Register(
            nameof(SubtitleColor), typeof(Brush), typeof(SubtitleOverlayControl),
            new PropertyMetadata(Brushes.White));

    /// <summary>자막 텍스트 색상 (기본값: White).</summary>
    public Brush SubtitleColor
    {
        get => (Brush)GetValue(SubtitleColorProperty);
        set => SetValue(SubtitleColorProperty, value);
    }

    public SubtitleOverlayControl()
    {
        InitializeComponent();
    }
}
