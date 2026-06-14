using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
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

/// <summary>
/// 그리기 오버레이 ViewModel.
/// IDrawingOverlaySource를 구현하여 RecordingSessionService가 프레임 합성에 활용합니다.
/// </summary>
public sealed partial class DrawingOverlayViewModel : ObservableObject, IDrawingOverlaySource
{
    // ── UI 참조 (Initialize()에서 주입) ───────────────────────────
    private InkCanvas? _inkCanvas;
    private Canvas?    _shapeCanvas;
    private Window?    _window;

    // ── Undo / Redo ───────────────────────────────────────────────
    private readonly Stack<IDrawingCommand> _undoStack = new();
    private readonly Stack<IDrawingCommand> _redoStack = new();

    // ── 진행 중인 도형 미리보기 ────────────────────────────────────
    private Point      _dragStart;
    private UIElement? _previewShape;
    private bool       _isDragging;

    // ── TextBox (텍스트 입력 중) ───────────────────────────────────
    private TextBox? _activeTextBox;

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
    [ObservableProperty] private bool   _isClickThrough  = false;

    // ── IDrawingOverlaySource ────────────────────────────────────

    public bool HasVisibleContent
    {
        get
        {
            if (_inkCanvas is null && _shapeCanvas is null) return false;
            return (_inkCanvas?.Strokes.Count > 0) ||
                   (_shapeCanvas?.Children.Count > 0);
        }
    }

    public async Task<byte[]?> GetPixelsAsync(int width, int height)
    {
        if (!HasVisibleContent || _inkCanvas is null || _shapeCanvas is null)
            return null;

        byte[]? pixels = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var rtb    = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                if (_inkCanvas.Strokes.Count > 0)
                    dc.DrawRectangle(
                        new VisualBrush(_inkCanvas) { Stretch = Stretch.Fill },
                        null, new Rect(0, 0, width, height));

                if (_shapeCanvas.Children.Count > 0)
                    dc.DrawRectangle(
                        new VisualBrush(_shapeCanvas) { Stretch = Stretch.Fill },
                        null, new Rect(0, 0, width, height));
            }

            rtb.Render(visual);
            pixels = new byte[width * height * 4];
            rtb.CopyPixels(pixels, width * 4, 0);

        }, DispatcherPriority.Render);

        return pixels;
    }

    // ── 초기화 (코드 비하인드에서 호출) ─────────────────────────────

    public void Initialize(InkCanvas inkCanvas, Canvas shapeCanvas, Window window)
    {
        _inkCanvas   = inkCanvas;
        _shapeCanvas = shapeCanvas;
        _window      = window;

        ApplyTool();

        _inkCanvas.StrokeCollected += OnStrokeCollected;
    }

    // ── 도구 선택 → InkCanvas 모드 전환 ────────────────────────────

    partial void OnSelectedToolChanged(DrawingTool value) => ApplyTool();

    private void ApplyTool()
    {
        if (_inkCanvas is null) return;

        if (SelectedTool == DrawingTool.Pen)
        {
            _inkCanvas.EditingMode   = InkCanvasEditingMode.Ink;
            _inkCanvas.DefaultDrawingAttributes = BuildDrawingAttributes();
        }
        else
        {
            _inkCanvas.EditingMode = InkCanvasEditingMode.None;
        }
    }

    private DrawingAttributes BuildDrawingAttributes() => new()
    {
        Color  = SelectedColor,
        Width  = StrokeThickness,
        Height = StrokeThickness,
        FitToCurve = true,
    };

    partial void OnSelectedColorChanged(Color value)
    {
        if (_inkCanvas is not null)
            _inkCanvas.DefaultDrawingAttributes.Color = value;
    }

    partial void OnStrokeThicknessChanged(double value)
    {
        if (_inkCanvas is not null)
        {
            _inkCanvas.DefaultDrawingAttributes.Width  = value;
            _inkCanvas.DefaultDrawingAttributes.Height = value;
        }
    }

    // ── Click-through 토글 ───────────────────────────────────────

    partial void OnIsClickThroughChanged(bool value)
        => (_window as Views.DrawingOverlayWindow)?.SetClickThrough(value);

    [RelayCommand]
    private void ToggleClickThrough() => IsClickThrough = !IsClickThrough;

    // ── 마우스 이벤트 (도형 그리기) ─────────────────────────────────

    public void OnMouseDown(Point pos)
    {
        if (SelectedTool == DrawingTool.Pen || _shapeCanvas is null) return;

        if (SelectedTool == DrawingTool.Text)
        {
            StartTextInput(pos);
            return;
        }

        CommitActiveTextBox();
        _dragStart   = pos;
        _isDragging  = true;
        _previewShape = CreatePreviewShape(pos);
        if (_previewShape is not null)
            _shapeCanvas.Children.Add(_previewShape);
    }

    public void OnMouseMove(Point pos)
    {
        if (!_isDragging || _previewShape is null || _shapeCanvas is null) return;
        UpdatePreviewShape(_previewShape, _dragStart, pos);
    }

    public void OnMouseUp(Point pos)
    {
        if (!_isDragging || _previewShape is null || _shapeCanvas is null)
        {
            _isDragging = false;
            return;
        }

        _isDragging = false;
        UpdatePreviewShape(_previewShape, _dragStart, pos);

        // 크기가 너무 작으면 무시
        var w = Math.Abs(pos.X - _dragStart.X);
        var h = Math.Abs(pos.Y - _dragStart.Y);
        if (w < 3 && h < 3)
        {
            _shapeCanvas.Children.Remove(_previewShape);
            _previewShape = null;
            return;
        }

        // 커맨드 히스토리에 기록
        var shape = _previewShape;
        _previewShape = null;
        PushCommand(new AddShapeCommand(_shapeCanvas, shape));
    }

    private UIElement? CreatePreviewShape(Point start)
    {
        var brush = new SolidColorBrush(SelectedColor);
        var thickness = (double)StrokeThickness;

        switch (SelectedTool)
        {
            case DrawingTool.Rectangle:
            {
                var rect = new Rectangle
                {
                    Stroke          = brush,
                    StrokeThickness = thickness,
                    Fill            = Brushes.Transparent,
                };
                Canvas.SetLeft(rect, start.X);
                Canvas.SetTop(rect, start.Y);
                return rect;
            }
            case DrawingTool.Ellipse:
            {
                var ellipse = new Ellipse
                {
                    Stroke          = brush,
                    StrokeThickness = thickness,
                    Fill            = Brushes.Transparent,
                };
                Canvas.SetLeft(ellipse, start.X);
                Canvas.SetTop(ellipse, start.Y);
                return ellipse;
            }
            case DrawingTool.Line:
            {
                return new Line
                {
                    Stroke          = brush,
                    StrokeThickness = thickness,
                    X1 = start.X, Y1 = start.Y,
                    X2 = start.X, Y2 = start.Y,
                };
            }
            case DrawingTool.Arrow:
                return CreateArrow(start, start, brush, thickness);

            default:
                return null;
        }
    }

    private static void UpdatePreviewShape(UIElement element, Point start, Point end)
    {
        switch (element)
        {
            case Rectangle rect:
            {
                var x = Math.Min(start.X, end.X);
                var y = Math.Min(start.Y, end.Y);
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Width  = Math.Abs(end.X - start.X);
                rect.Height = Math.Abs(end.Y - start.Y);
                break;
            }
            case Ellipse ellipse:
            {
                var x = Math.Min(start.X, end.X);
                var y = Math.Min(start.Y, end.Y);
                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                ellipse.Width  = Math.Abs(end.X - start.X);
                ellipse.Height = Math.Abs(end.Y - start.Y);
                break;
            }
            case Line line:
                line.X2 = end.X;
                line.Y2 = end.Y;
                break;

            case Canvas arrowCanvas:
                UpdateArrow(arrowCanvas, start, end);
                break;
        }
    }

    // ── 화살표 (Canvas 컨테이너 안에 Line + Path) ───────────────────

    private static Canvas CreateArrow(Point start, Point end, Brush brush, double thickness)
    {
        var line = new Line
        {
            Stroke          = brush,
            StrokeThickness = thickness,
            X1 = start.X, Y1 = start.Y,
            X2 = end.X,   Y2 = end.Y,
        };
        var head = BuildArrowHead(start, end, brush, thickness);
        var container = new Canvas();
        container.Children.Add(line);
        container.Children.Add(head);
        return container;
    }

    private static void UpdateArrow(Canvas container, Point start, Point end)
    {
        if (container.Children.Count < 2) return;
        Line? line = container.Children[0] as Line;
        if (line is not null)
        {
            line.X1 = start.X; line.Y1 = start.Y;
            line.X2 = end.X;   line.Y2 = end.Y;
        }
        if (container.Children[1] is System.Windows.Shapes.Path oldHead)
        {
            container.Children.Remove(oldHead);
            container.Children.Add(BuildArrowHead(start, end, line?.Stroke ?? Brushes.Red, line?.StrokeThickness ?? 2));
        }
    }

    private static System.Windows.Shapes.Path BuildArrowHead(Point start, Point end, Brush brush, double thickness)
    {
        var dir   = end - start;
        var len   = dir.Length;
        if (len < 1) dir = new Vector(1, 0);
        else dir /= len;

        var perp      = new Vector(-dir.Y, dir.X);
        double arrowL = Math.Max(12, thickness * 4);
        double arrowW = arrowL * 0.4;

        var p1 = end - dir * arrowL + perp * arrowW;
        var p2 = end - dir * arrowL - perp * arrowW;

        var geo = new PathGeometry(new[]
        {
            new PathFigure(end, new[] { new PolyLineSegment(new[] { p1, p2, end }, true) }, true)
        });

        return new System.Windows.Shapes.Path { Fill = brush, Data = geo };
    }

    // ── 텍스트 도구 ─────────────────────────────────────────────────

    private void StartTextInput(Point pos)
    {
        if (_shapeCanvas is null) return;
        CommitActiveTextBox();

        var tb = new TextBox
        {
            Background   = Brushes.Transparent,
            BorderBrush  = new SolidColorBrush(SelectedColor) { Opacity = 0.5 },
            BorderThickness = new Thickness(1),
            Foreground   = new SolidColorBrush(SelectedColor),
            FontSize     = Math.Max(14, StrokeThickness * 5),
            MinWidth     = 80,
            CaretBrush   = new SolidColorBrush(SelectedColor),
            AcceptsReturn = false,
        };

        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb, pos.Y);

        tb.KeyDown += OnTextBoxKeyDown;
        tb.LostFocus += OnTextBoxLostFocus;

        _shapeCanvas.Children.Add(tb);
        _activeTextBox = tb;
        tb.Focus();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Escape)
            CommitActiveTextBox();
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
        => CommitActiveTextBox();

    private void CommitActiveTextBox()
    {
        if (_activeTextBox is null || _shapeCanvas is null) return;

        var tb = _activeTextBox;
        _activeTextBox = null;

        tb.KeyDown    -= OnTextBoxKeyDown;
        tb.LostFocus  -= OnTextBoxLostFocus;

        if (string.IsNullOrWhiteSpace(tb.Text))
        {
            _shapeCanvas.Children.Remove(tb);
            return;
        }

        // TextBox → TextBlock으로 고정
        var block = new TextBlock
        {
            Text       = tb.Text,
            Foreground = new SolidColorBrush(SelectedColor),
            FontSize   = tb.FontSize,
        };

        Canvas.SetLeft(block, Canvas.GetLeft(tb));
        Canvas.SetTop(block,  Canvas.GetTop(tb));

        _shapeCanvas.Children.Remove(tb);
        _shapeCanvas.Children.Add(block);
        PushCommand(new AddShapeCommand(_shapeCanvas, block));
    }

    // ── InkCanvas StrokeCollected 핸들러 ────────────────────────────

    private void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        // InkCanvas가 이미 스트로크를 추가했으므로 히스토리만 기록
        _redoStack.Clear();
        _undoStack.Push(new AddStrokeCommand(_inkCanvas!, e.Stroke));
        RefreshUndoRedoState();
    }

    // ── Undo / Redo ───────────────────────────────────────────────

    private void PushCommand(IDrawingCommand cmd)
    {
        cmd.Execute();
        _redoStack.Clear();
        _undoStack.Push(cmd);
        RefreshUndoRedoState();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!_undoStack.TryPop(out var cmd)) return;
        cmd.Undo();
        _redoStack.Push(cmd);
        RefreshUndoRedoState();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!_redoStack.TryPop(out var cmd)) return;
        cmd.Execute();
        _undoStack.Push(cmd);
        RefreshUndoRedoState();
    }

    [RelayCommand]
    private void Clear()
    {
        if (_inkCanvas is null || _shapeCanvas is null) return;
        if (_inkCanvas.Strokes.Count == 0 && _shapeCanvas.Children.Count == 0) return;

        CommitActiveTextBox();
        var cmd = new ClearCommand(_inkCanvas, _shapeCanvas);
        cmd.Execute();
        _undoStack.Push(cmd);
        _redoStack.Clear();
        RefreshUndoRedoState();
    }

    private void RefreshUndoRedoState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    // ── 도구 선택 커맨드 ───────────────────────────────────────────

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        if (Enum.TryParse<DrawingTool>(toolName, out var tool))
            SelectedTool = tool;
    }

    // ── 색상 프리셋 커맨드 ─────────────────────────────────────────

    [RelayCommand]
    private void SelectColor(string hex)
    {
        try { SelectedColor = (Color)ColorConverter.ConvertFromString(hex); }
        catch { }
    }

    // ── 굵기 프리셋 커맨드 ─────────────────────────────────────────

    [RelayCommand]
    private void SelectThickness(string thicknessStr)
    {
        if (double.TryParse(thicknessStr, out var t)) StrokeThickness = t;
    }
}
