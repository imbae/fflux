namespace fflux.UI.Modules.ScreenRecorder.Drawing.Commands;

public interface IDrawingCommand
{
    void Execute();
    void Undo();
}
