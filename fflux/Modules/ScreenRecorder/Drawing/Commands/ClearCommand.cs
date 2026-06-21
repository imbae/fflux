using System.Windows;
using System.Windows.Controls;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

public sealed class ClearCommand : IDrawingCommand
{
    private readonly Canvas      _canvas;
    private readonly UIElement[] _savedElements;

    public ClearCommand(Canvas canvas)
    {
        _canvas        = canvas;
        _savedElements = canvas.Children.OfType<UIElement>().ToArray();
    }

    public void Execute() => _canvas.Children.Clear();

    public void Undo()
    {
        foreach (var e in _savedElements)
            _canvas.Children.Add(e);
    }
}
