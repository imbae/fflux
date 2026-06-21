using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using fflux.Core.Abstractions;
using fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

namespace fflux.UI.Modules.ScreenRecorder.Drawing;

public sealed partial class DrawingOverlayViewModel : ObservableObject, IDrawingOverlaySource
{
    // ── UI 참조 ───────────────────────────────────────────────────
    private Canvas? _canvas;
    private Window? _window;

    // ── Undo / Redo ───────────────────────────────────────────────
    private readonly Stack<IDrawingCommand> _undoStack = new();
    private readonly Stack<IDrawingCommand> _redoStack = new();

    // ── 진행 중 상태 ──────────────────────────────────────────────
    private Polyline? _currentStroke;   // 펜: 그리는 중인 Polyline
    private Point      _dragStart;       // 도형: 드래그 시작점
    private UIElement? _previewShape;    // 도형: 미리보기 요소
    private bool       _isDragging;
    private TextBox?   _activeTextBox;

    // ── 오버레이 픽셀 캐시 ────────────────────────────────────────
    // 캔버스가 변경됐을 때만 재렌더링하고, 나머지 프레임은 캐시를 즉시 반환해
    // 매 프레임 UI 스레드 점유와 8MB 픽셀 복사를 방지한다.
    // _isDirty: UI 스레드가 쓰고 캡처 루프(BG 스레드)가 읽으므로 volatile 필수.
    private volatile bool _isDirty;
    private byte[]?       _cachedPixels;
    private int           _cachedWidth, _cachedHeight;

    // ── 관찰 가능 속성 ────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool _canRedo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolPen), nameof(IsToolRectangle),
        nameof(IsToolEllipse), nameof(IsToolLine), nameof(IsToolArrow), nameof(IsToolText))]
    private DrawingTool _selectedTool = DrawingTool.Pen;

    public bool IsToolPen       => SelectedTool == DrawingTool.Pen;
    public bool IsToolRectangle => SelectedTool == DrawingTool.Rectangle;
    public bool IsToolEllipse   => SelectedTool == DrawingTool.Ellipse;
    public bool IsToolLine      => SelectedTool == DrawingTool.Line;
    public bool IsToolArrow     => SelectedTool == DrawingTool.Arrow;
    public bool IsToolText      => SelectedTool == DrawingTool.Text;

    [ObservableProperty] private Color  _selectedColor   = Colors.Red;
    [ObservableProperty] private double _strokeThickness = 3.0;

    // ── IDrawingOverlaySource ────────────────────────────────────

    // volatile: 백그라운드 스레드(캡처 루프)에서 안전하게 읽기 위한 장치.
    // _canvas.Children.Count를 백그라운드 스레드에서 직접 읽으면 WPF 스레드 어피니티
    // 위반으로 항상 0을 반환하거나 예외가 발생하므로, UI 스레드에서만 갱신한다.
    private volatile bool _hasDrawings;

    public bool HasVisibleContent => _hasDrawings;

    public async Task<byte[]?> GetPixelsAsync(int width, int height)
    {
        if (!_hasDrawings || _canvas is null) return null;

        // 캔버스 변경 없고 치수도 같으면 캐시 즉시 반환 — UI 스레드 점유 없음
        if (!_isDirty && _cachedPixels is not null
            && _cachedWidth == width && _cachedHeight == height)
            return _cachedPixels;

        byte[]? pixels = null;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var canvas = _canvas;
            if (canvas is null || canvas.Children.Count == 0) return;
            if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;

            // ViewboxUnits=Absolute: 캔버스 전체 좌표계 기준 렌더링 (바운딩박스 왜곡 방지)
            var brush = new VisualBrush(canvas)
            {
                Stretch      = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox      = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight),
            };

            var rtb    = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            rtb.Render(visual);

            // 치수가 같으면 버퍼 재사용해 GC 압박 방지
            if (_cachedPixels is null || _cachedPixels.Length != width * height * 4)
                _cachedPixels = new byte[width * height * 4];
            rtb.CopyPixels(_cachedPixels, width * 4, 0);

            _cachedWidth  = width;
            _cachedHeight = height;
            pixels        = _cachedPixels;

            // volatile write: BG 스레드가 _isDirty=false 읽을 때 _cachedPixels 갱신도 보장
            _isDirty = false;
        }, DispatcherPriority.Render);

        return pixels ?? _cachedPixels;
    }

    // 캔버스 상태 동기화. UI 스레드에서만 호출.
    private void RefreshHasDrawings()
    {
        _hasDrawings = _canvas is not null && _canvas.Children.Count > 0;
        _isDirty     = true;  // 캔버스 변경 → 다음 GetPixelsAsync에서 재렌더링
        if (!_hasDrawings)
            _cachedPixels = null;  // 내용 없으면 캐시 해제
    }

    // ── 초기화 / 정리 ────────────────────────────────────────────

    public void Initialize(Canvas canvas, Window window)
    {
        Cleanup();
        _canvas = canvas;
        _window = window;
    }

    public void Cleanup()
    {
        _currentStroke = null;
        _previewShape  = null;
        _isDragging    = false;

        if (_activeTextBox is not null)
        {
            _activeTextBox.KeyDown   -= OnTextBoxKeyDown;
            _activeTextBox.LostFocus -= OnTextBoxLostFocus;
            _activeTextBox = null;
        }

        _undoStack.Clear();
        _redoStack.Clear();
        RefreshUndoRedoState();

        _canvas       = null;
        _window       = null;
        _hasDrawings  = false;
        _isDirty      = false;
        _cachedPixels = null;
    }

    // ── 마우스 이벤트 (전역 훅 → DrawingOverlayWindow가 호출) ────

    public void OnMouseDown(Point pos)
    {
        if (_canvas is null) return;

        switch (SelectedTool)
        {
            case DrawingTool.Pen:
                StartPenStroke(pos);
                break;

            case DrawingTool.Text:
                CommitActiveTextBox();
                StartTextInput(pos);
                break;

            default:
                CommitActiveTextBox();
                _dragStart    = pos;
                _isDragging   = true;
                _previewShape = CreatePreviewShape(pos);
                if (_previewShape is not null)
                    _canvas.Children.Add(_previewShape);
                break;
        }
    }

    public void OnMouseMove(Point pos)
    {
        if (SelectedTool == DrawingTool.Pen)
            ContinuePenStroke(pos);
        else if (_isDragging && _previewShape is not null)
        {
            UpdatePreviewShape(_previewShape, _dragStart, pos);
            _isDirty = true;
        }
    }

    public void OnMouseUp(Point pos)
    {
        if (SelectedTool == DrawingTool.Pen)
        {
            FinishPenStroke();
            return;
        }

        if (!_isDragging) return;
        _isDragging = false;

        if (_previewShape is null) return;
        UpdatePreviewShape(_previewShape, _dragStart, pos);

        var w = Math.Abs(pos.X - _dragStart.X);
        var h = Math.Abs(pos.Y - _dragStart.Y);
        if (w < 3 && h < 3)
        {
            _canvas?.Children.Remove(_previewShape);
            _previewShape = null;
            return;
        }

        // ★ 이미 Canvas에 추가된 요소 → RegisterCommand (Execute 재호출 금지)
        var shape = _previewShape;
        _previewShape = null;
        RegisterCommand(new AddShapeCommand(_canvas!, shape));
    }

    // ── 펜 그리기 (Polyline) ──────────────────────────────────────

    private void StartPenStroke(Point pos)
    {
        _currentStroke = new Polyline
        {
            Stroke             = new SolidColorBrush(SelectedColor),
            StrokeThickness    = StrokeThickness,
            StrokeLineJoin     = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            IsHitTestVisible   = false,
        };
        _currentStroke.Points.Add(pos);
        _canvas!.Children.Add(_currentStroke);  // 라이브 프리뷰용 선 추가
    }

    private void ContinuePenStroke(Point pos)
    {
        if (_currentStroke is null) return;
        _currentStroke.Points.Add(pos);
        _isDirty = true;
    }

    private void FinishPenStroke()
    {
        if (_currentStroke is null || _canvas is null) return;
        var stroke = _currentStroke;
        _currentStroke = null;

        if (stroke.Points.Count < 2)
        {
            _canvas.Children.Remove(stroke);
            return;
        }

        // ★ 이미 Canvas에 추가된 Polyline → RegisterCommand (Execute 재호출 금지)
        RegisterCommand(new AddShapeCommand(_canvas, stroke));
    }

    // ── 도형 생성 / 업데이트 ─────────────────────────────────────

    private UIElement? CreatePreviewShape(Point start)
    {
        var brush     = new SolidColorBrush(SelectedColor);
        var thickness = StrokeThickness;

        return SelectedTool switch
        {
            DrawingTool.Rectangle => BuildRect(start, brush, thickness),
            DrawingTool.Ellipse   => BuildEllipse(start, brush, thickness),
            DrawingTool.Line      => BuildLine(start, brush, thickness),
            DrawingTool.Arrow     => BuildArrow(start, start, brush, thickness),
            _                     => null,
        };
    }

    private static Rectangle BuildRect(Point start, Brush brush, double t)
    {
        var r = new Rectangle
        {
            Stroke = brush, StrokeThickness = t,
            Fill = Brushes.Transparent, IsHitTestVisible = false,
        };
        Canvas.SetLeft(r, start.X);
        Canvas.SetTop(r, start.Y);
        return r;
    }

    private static Ellipse BuildEllipse(Point start, Brush brush, double t)
    {
        var e = new Ellipse
        {
            Stroke = brush, StrokeThickness = t,
            Fill = Brushes.Transparent, IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, start.X);
        Canvas.SetTop(e, start.Y);
        return e;
    }

    private static Line BuildLine(Point start, Brush brush, double t) => new()
    {
        Stroke = brush, StrokeThickness = t,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
        X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y,
    };

    private static Canvas BuildArrow(Point start, Point end, Brush brush, double t)
    {
        var line = new Line
        {
            Stroke = brush, StrokeThickness = t,
            StrokeStartLineCap = PenLineCap.Round,
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
        };
        var container = new Canvas { IsHitTestVisible = false };
        container.Children.Add(line);
        container.Children.Add(BuildArrowHead(start, end, brush, t));
        return container;
    }

    private static void UpdatePreviewShape(UIElement element, Point start, Point end)
    {
        switch (element)
        {
            case Rectangle rect:
                Canvas.SetLeft(rect, Math.Min(start.X, end.X));
                Canvas.SetTop(rect,  Math.Min(start.Y, end.Y));
                rect.Width  = Math.Abs(end.X - start.X);
                rect.Height = Math.Abs(end.Y - start.Y);
                break;

            case Ellipse ellipse:
                Canvas.SetLeft(ellipse, Math.Min(start.X, end.X));
                Canvas.SetTop(ellipse,  Math.Min(start.Y, end.Y));
                ellipse.Width  = Math.Abs(end.X - start.X);
                ellipse.Height = Math.Abs(end.Y - start.Y);
                break;

            case Line line:
                line.X2 = end.X; line.Y2 = end.Y;
                break;

            case Canvas arrow:
                UpdateArrow(arrow, start, end);
                break;
        }
    }

    // ── 화살표 ───────────────────────────────────────────────────

    private static void UpdateArrow(Canvas container, Point start, Point end)
    {
        if (container.Children.Count < 2) return;
        if (container.Children[0] is Line line)
        {
            line.X1 = start.X; line.Y1 = start.Y;
            line.X2 = end.X;   line.Y2 = end.Y;
        }
        if (container.Children[1] is System.Windows.Shapes.Path old)
        {
            container.Children.Remove(old);
            if (container.Children[0] is Line l)
                container.Children.Add(BuildArrowHead(start, end, l.Stroke, l.StrokeThickness));
        }
    }

    private static System.Windows.Shapes.Path BuildArrowHead(
        Point start, Point end, Brush brush, double t)
    {
        var dir = end - start;
        var len = dir.Length;
        if (len < 1) dir = new Vector(1, 0); else dir /= len;

        var perp = new Vector(-dir.Y, dir.X);
        double arL = Math.Max(12, t * 4), arW = arL * 0.4;

        var geo = new PathGeometry(new[]
        {
            new PathFigure(end,
                new[] { new PolyLineSegment(
                    new[] { end - dir * arL + perp * arW, end - dir * arL - perp * arW, end }, true) },
                true)
        });
        return new System.Windows.Shapes.Path { Fill = brush, Data = geo };
    }

    // ── 텍스트 ───────────────────────────────────────────────────

    private void StartTextInput(Point pos)
    {
        if (_canvas is null) return;
        var tb = new TextBox
        {
            Background      = Brushes.Transparent,
            BorderBrush     = new SolidColorBrush(SelectedColor) { Opacity = 0.5 },
            BorderThickness = new Thickness(1),
            Foreground      = new SolidColorBrush(SelectedColor),
            FontSize        = Math.Max(14, StrokeThickness * 5),
            MinWidth        = 80,
            CaretBrush      = new SolidColorBrush(SelectedColor),
            AcceptsReturn   = false,
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb,  pos.Y);
        tb.KeyDown   += OnTextBoxKeyDown;
        tb.LostFocus += OnTextBoxLostFocus;
        _canvas.Children.Add(tb);
        _activeTextBox = tb;
        tb.Focus();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Escape) CommitActiveTextBox();
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
        => CommitActiveTextBox();

    private void CommitActiveTextBox()
    {
        if (_activeTextBox is null || _canvas is null) return;
        var tb = _activeTextBox;
        _activeTextBox = null;
        tb.KeyDown   -= OnTextBoxKeyDown;
        tb.LostFocus -= OnTextBoxLostFocus;

        if (string.IsNullOrWhiteSpace(tb.Text))
        {
            _canvas.Children.Remove(tb);
            return;
        }

        var block = new TextBlock
        {
            Text             = tb.Text,
            Foreground       = new SolidColorBrush(SelectedColor),
            FontSize         = tb.FontSize,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(block, Canvas.GetLeft(tb));
        Canvas.SetTop(block,  Canvas.GetTop(tb));
        _canvas.Children.Remove(tb);
        _canvas.Children.Add(block);

        // ★ 이미 Canvas에 추가된 TextBlock → RegisterCommand
        RegisterCommand(new AddShapeCommand(_canvas, block));
    }

    // ── Undo / Redo ───────────────────────────────────────────────

    /// <summary>
    /// 이미 Canvas에 추가된 요소를 Undo 스택에만 등록합니다.
    /// Execute()를 호출하지 않아 이중 추가 예외를 방지합니다.
    /// </summary>
    private void RegisterCommand(IDrawingCommand cmd)
    {
        _redoStack.Clear();
        _undoStack.Push(cmd);
        RefreshUndoRedoState();
        RefreshHasDrawings();
    }

    /// <summary>Canvas에 아직 추가되지 않은 커맨드를 Execute한 뒤 스택에 등록합니다.</summary>
    private void PushCommand(IDrawingCommand cmd)
    {
        cmd.Execute();
        _redoStack.Clear();
        _undoStack.Push(cmd);
        RefreshUndoRedoState();
        RefreshHasDrawings();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!_undoStack.TryPop(out var cmd)) return;
        cmd.Undo();
        _redoStack.Push(cmd);
        RefreshUndoRedoState();
        RefreshHasDrawings();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!_redoStack.TryPop(out var cmd)) return;
        cmd.Execute();
        _undoStack.Push(cmd);
        RefreshUndoRedoState();
        RefreshHasDrawings();
    }

    [RelayCommand]
    private void Clear()
    {
        if (_canvas is null || _canvas.Children.Count == 0) return;
        CommitActiveTextBox();
        PushCommand(new ClearCommand(_canvas));
    }

    private void RefreshUndoRedoState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    // ── 도구 / 색상 / 굵기 커맨드 ────────────────────────────────

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        if (Enum.TryParse<DrawingTool>(toolName, out var tool))
            SelectedTool = tool;
    }

    [RelayCommand]
    private void SelectColor(string hex)
    {
        try { SelectedColor = (Color)ColorConverter.ConvertFromString(hex); }
        catch { }
    }

    [RelayCommand]
    private void SelectThickness(string v)
    {
        if (double.TryParse(v, out var t)) StrokeThickness = t;
    }
}
