using System.Windows.Controls;
using System.Windows.Ink;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

public sealed class AddStrokeCommand : IDrawingCommand
{
    private readonly InkCanvas _canvas;
    private readonly Stroke    _stroke;

    public AddStrokeCommand(InkCanvas canvas, Stroke stroke)
    {
        _canvas = canvas;
        _stroke = stroke;
    }

    public void Execute() => _canvas.Strokes.Add(_stroke);
    public void Undo()    => _canvas.Strokes.Remove(_stroke);
}
