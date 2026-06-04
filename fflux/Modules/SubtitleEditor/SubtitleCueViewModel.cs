using System.Windows.Media;
using fflux.Core.Models;

namespace fflux.UI.Modules.SubtitleEditor;

/// <summary>
/// DataGrid 한 행에 해당하는 자막 큐(cue)의 편집 가능한 ViewModel.
/// <para>
/// <see cref="StartText"/> / <see cref="EndText"/>는 "HH:MM:SS,mmm" 형식의 자유 텍스트이며,
/// 파싱에 실패하면 <see cref="HasStartError"/> / <see cref="HasEndError"/>가 true가 됩니다.
/// <see cref="ColorText"/>는 "#RRGGBB" 형식이거나 빈 문자열(기본 흰색)입니다.
/// </para>
/// </summary>
public sealed partial class SubtitleCueViewModel : ObservableObject
{
    // ── 표시 인덱스 (1-based) ────────────────────────────────────────
    [ObservableProperty]
    private int _index;

    // ── 타임스탬프 (편집 가능 문자열) ────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Start))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _startText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(End))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _endText = "";

    // ── 자막 텍스트 ──────────────────────────────────────────────────
    [ObservableProperty]
    private string _text = "";

    // ── 자막 색상 ("#RRGGBB" 또는 빈 문자열 = 기본 흰색) ─────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorBrush))]
    [NotifyPropertyChangedFor(nameof(HasCustomColor))]
    private string _colorText = "";

    // ColorBrush 캐시: ColorText가 바뀔 때만 새 브러시를 생성합니다.
    // null = 아직 계산 안 됨 (lazy init).
    private Brush? _cachedColorBrush;

    /// <summary>ColorText → Brush 변환. 비어 있거나 파싱 실패 시 White.</summary>
    public Brush ColorBrush => _cachedColorBrush ??= BuildColorBrush();

    private Brush BuildColorBrush()
    {
        if (string.IsNullOrWhiteSpace(ColorText)) return Brushes.White;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(ColorText);
            var brush = new SolidColorBrush(color);
            brush.Freeze(); // 불변 브러시 → GC finalizer 큐 부담 없음
            return brush;
        }
        catch { return Brushes.White; }
    }

    partial void OnColorTextChanged(string value)
        => _cachedColorBrush = null; // ColorText 변경 시 캐시 무효화

    /// <summary>커스텀 색상이 지정된 경우 true (미리보기 표시용).</summary>
    public bool HasCustomColor => !string.IsNullOrWhiteSpace(ColorText);

    // ── 현재 재생 중인 큐 여부 (플레이어 위치 기반 하이라이트) ─────
    [ObservableProperty]
    private bool _isActive;

    // ── 타임스탬프 유효성 ────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private bool _hasStartError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private bool _hasEndError;

    /// <summary>Start 또는 End 타임스탬프가 잘못된 형식이면 true.</summary>
    public bool HasError => HasStartError || HasEndError;

    /// <summary>StartText를 파싱한 TimeSpan. 파싱 실패 시 null.</summary>
    public TimeSpan? Start => ParseTimestamp(StartText);

    /// <summary>EndText를 파싱한 TimeSpan. 파싱 실패 시 null.</summary>
    public TimeSpan? End => ParseTimestamp(EndText);

    partial void OnStartTextChanged(string value) => HasStartError = ParseTimestamp(value) is null;
    partial void OnEndTextChanged(string value)   => HasEndError   = ParseTimestamp(value) is null;

    // ── 팩토리 ─────────────────────────────────────────────────────

    /// <summary><see cref="SubtitleEntry"/>에서 편집 가능한 ViewModel을 생성합니다.</summary>
    public static SubtitleCueViewModel FromEntry(int index, SubtitleEntry entry)
        => new()
        {
            Index     = index,
            StartText = FormatTimestamp(entry.Start),
            EndText   = FormatTimestamp(entry.End),
            Text      = entry.Text,
            ColorText = entry.Color ?? "",
        };

    /// <summary>
    /// 편집 결과를 <see cref="SubtitleEntry"/>로 변환합니다.
    /// 타임스탬프가 유효하지 않거나 텍스트가 비어 있으면 null을 반환합니다.
    /// </summary>
    public SubtitleEntry? ToEntry()
    {
        var s = Start;
        var e = End;
        if (s is null || e is null || string.IsNullOrWhiteSpace(Text))
            return null;

        // #RRGGBB 형식만 허용합니다. 그 외 값(색상명, 잘못된 hex 등)은 저장하지 않습니다.
        // 저장 후 재로드 시 SrtParser.NormalizeColor가 동일 기준으로 필터링하므로
        // 여기서 먼저 걸러야 라운드트립 손실이 없습니다.
        var color = IsValidHexColor(ColorText) ? ColorText.Trim().ToUpperInvariant() : null;
        return new SubtitleEntry(s.Value, e.Value, Text.Trim(), color);
    }

    /// <summary>#RRGGBB 형식인지 확인합니다.</summary>
    public static bool IsValidHexColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length != 7 || t[0] != '#') return false;
        foreach (var c in t.AsSpan(1))
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }

    // ── 타임스탬프 포맷 헬퍼 (public: XAML 코드-비하인드에서도 사용) ──

    /// <summary>TimeSpan → "HH:MM:SS,mmm" (SRT 표준 형식)</summary>
    public static string FormatTimestamp(TimeSpan ts)
        => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";

    /// <summary>
    /// "HH:MM:SS,mmm" 또는 "HH:MM:SS.mmm" 또는 "MM:SS,mmm" → TimeSpan.
    /// 파싱 실패 시 null.
    /// </summary>
    public static TimeSpan? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var t = text.Trim().Replace(',', '.');
        if (TimeSpan.TryParse(t, out var ts)) return ts;

        // MM:SS.mmm 형식 (시간 없음) 지원
        if (t.Count(c => c == ':') == 1)
        {
            if (TimeSpan.TryParse("00:" + t, out ts)) return ts;
        }

        return null;
    }

    // ── 색상 프리셋 (static) ─────────────────────────────────────────

    /// <summary>UI에서 제공하는 색상 프리셋 목록.</summary>
    public static IReadOnlyList<ColorPreset> ColorPresets { get; } =
    [
        new("기본 (흰색)",  ""        ),
        new("흰색",        "#FFFFFF"  ),
        new("노랑",        "#FFE000"  ),
        new("하늘색",      "#00E5FF"  ),
        new("초록",        "#00E676"  ),
        new("주황",        "#FF9100"  ),
        new("빨강",        "#FF5252"  ),
        new("분홍",        "#FF80AB"  ),
    ];

    /// <summary>색상 프리셋 항목.</summary>
    public record ColorPreset(string Name, string Value);
}
