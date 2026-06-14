using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

public enum RecordingState { Idle, Recording, Paused, Stopping }

public interface IRecordingSessionService : IAsyncDisposable
{
    RecordingState State           { get; }
    TimeSpan       Elapsed         { get; }
    string?        OutputFilePath  { get; }

    event EventHandler<RecordingState>? StateChanged;
    event EventHandler<TimeSpan>?       ElapsedUpdated;

    Task         StartAsync(RecordingSettings settings, CancellationToken ct = default);
    Task         PauseAsync();
    Task         ResumeAsync();
    Task<string?> StopAsync();
}
