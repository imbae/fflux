using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace fflux.UI.Shared.Controls;

/// <summary>
/// 타임코드(00:00:00.000) 형식 입력을 지원하는 커스텀 TextBox.
/// Value(TimeSpan)와 양방향 바인딩하며, ↑↓ 키로 세그먼트 값을 조절합니다.
/// </summary>
public sealed class TimecodeTextBox : TextBox
{
    // ── 타임코드 파싱 패턴: hh:mm:ss.fff ───────────────────
    private static readonly Regex TimecodeRegex =
        new(@"^(\d{1,2}):([0-5]\d):([0-5]\d)\.(\d{1,3})$", RegexOptions.Compiled);

    private bool _isUpdatingText;

    // ── Value DependencyProperty ────────────────────────────
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(TimeSpan),
            typeof(TimecodeTextBox),
            new FrameworkPropertyMetadata(
                TimeSpan.Zero,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    /// <summary>현재 타임코드 값 (TimeSpan)</summary>
    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // ── MaxValue DependencyProperty ─────────────────────────
    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(
            nameof(MaxValue),
            typeof(TimeSpan),
            typeof(TimecodeTextBox),
            new PropertyMetadata(TimeSpan.MaxValue));

    /// <summary>최대 허용 TimeSpan 값 (기본: TimeSpan.MaxValue)</summary>
    public TimeSpan MaxValue
    {
        get => (TimeSpan)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    // ── 생성자 ──────────────────────────────────────────────
    static TimecodeTextBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TimecodeTextBox),
            new FrameworkPropertyMetadata(typeof(TextBox)));
    }

    public TimecodeTextBox()
    {
        Text = FormatTimeSpan(TimeSpan.Zero);
        FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New");
        TextAlignment = TextAlignment.Center;
        MaxLength = 12; // "00:00:00.000"
        VerticalContentAlignment = VerticalAlignment.Center;

        LostFocus += OnLostFocus;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ── Value → Text 동기화 ─────────────────────────────────
    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TimecodeTextBox box) return;
        if (box._isUpdatingText) return;

        box._isUpdatingText = true;
        box.Text = FormatTimeSpan((TimeSpan)e.NewValue);
        box._isUpdatingText = false;
    }

    // ── Text → Value 동기화 (포커스 잃을 때) ───────────────
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (TryParseTimecode(Text, out var ts))
        {
            _isUpdatingText = true;
            Value = Clamp(ts);
            Text = FormatTimeSpan(Value);
            _isUpdatingText = false;
        }
        else
        {
            // 파싱 실패 시 현재 Value로 복원
            Text = FormatTimeSpan(Value);
        }
    }

    // ── ↑↓ 키로 세그먼트 조절 ──────────────────────────────
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;

        var delta = e.Key == Key.Up ? 1 : -1;
        var segment = GetSegmentAtCaret();
        var newValue = AdjustSegment(Value, segment, delta);
        Value = Clamp(newValue);

        e.Handled = true;
    }

    // ── 캐럿 위치 기반 세그먼트 판별 ───────────────────────
    // "HH:MM:SS.mmm"
    //  01 23 45 67 890 (인덱스)
    //  ^hours^  ^min^ ^sec^ ^ms^
    private TimecodeSegment GetSegmentAtCaret()
    {
        int caret = CaretIndex;
        return caret switch
        {
            <= 2 => TimecodeSegment.Hours,
            <= 5 => TimecodeSegment.Minutes,
            <= 8 => TimecodeSegment.Seconds,
            _    => TimecodeSegment.Milliseconds
        };
    }

    private static TimeSpan AdjustSegment(TimeSpan ts, TimecodeSegment segment, int delta)
        => segment switch
        {
            TimecodeSegment.Hours        => ts + TimeSpan.FromHours(delta),
            TimecodeSegment.Minutes      => ts + TimeSpan.FromMinutes(delta),
            TimecodeSegment.Seconds      => ts + TimeSpan.FromSeconds(delta),
            TimecodeSegment.Milliseconds => ts + TimeSpan.FromMilliseconds(delta * 100),
            _ => ts
        };

    private TimeSpan Clamp(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero)   return TimeSpan.Zero;
        if (ts > MaxValue)        return MaxValue;
        return ts;
    }

    // ── 포맷 / 파싱 헬퍼 ────────────────────────────────────
    private static string FormatTimeSpan(TimeSpan ts)
        => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";

    private static bool TryParseTimecode(string? text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = TimecodeRegex.Match(text.Trim());
        if (!m.Success) return false;

        int h  = int.Parse(m.Groups[1].Value);
        int mi = int.Parse(m.Groups[2].Value);
        int s  = int.Parse(m.Groups[3].Value);

        // 밀리초 문자열을 3자리로 정규화 (예: "5" → 500, "05" → 50)
        var msStr = m.Groups[4].Value.PadRight(3, '0');
        int ms = int.Parse(msStr);

        result = new TimeSpan(0, h, mi, s, ms);
        return true;
    }

    private enum TimecodeSegment { Hours, Minutes, Seconds, Milliseconds }
}
