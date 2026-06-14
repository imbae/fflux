using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

public sealed class ClearCommand : IDrawingCommand
{
    private readonly InkCanvas   _inkCanvas;
    private readonly Canvas      _shapeCanvas;
    private readonly StrokeCollection _savedStrokes;
    private readonly UIElement[] _savedShapes;

    public ClearCommand(InkCanvas inkCanvas, Canvas shapeCanvas)
    {
        _inkCanvas   = inkCanvas;
        _shapeCanvas = shapeCanvas;
        _savedStrokes = inkCanvas.Strokes.Clone();
        _savedShapes  = shapeCanvas.Children.OfType<UIElement>().ToArray();
    }

    public void Execute()
    {
        _inkCanvas.Strokes.Clear();
        _shapeCanvas.Children.Clear();
    }

    public void Undo()
    {
        foreach (var s in _savedStrokes) _inkCanvas.Strokes.Add(s);
        foreach (var e in _savedShapes)  _shapeCanvas.Children.Add(e);
    }
}
