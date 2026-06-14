using System.Windows;
using System.Windows.Controls;

namespace fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

public sealed class AddShapeCommand : IDrawingCommand
{
    private readonly Canvas    _canvas;
    private readonly UIElement _element;

    public AddShapeCommand(Canvas canvas, UIElement element)
    {
        _canvas  = canvas;
        _element = element;
    }

    public void Execute() => _canvas.Children.Add(_element);
    public void Undo()    => _canvas.Children.Remove(_element);
}
