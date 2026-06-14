using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using fflux.UI.Modules.ScreenRecorder.Core.Services;

namespace fflux.UI.Modules.ScreenRecorder.Views;

public sealed partial class RecorderOverlayViewModel : ObservableObject, IDisposable
{
    private readonly IRecordingSessionService _session;
    private readonly Window                   _window;

    [ObservableProperty] private string _elapsedText = "00:00:00";
    [ObservableProperty] private bool   _isPaused    = false;

    public RecorderOverlayViewModel(IRecordingSessionService session, Window window)
    {
        _session = session;
        _window  = window;

        _session.ElapsedUpdated += OnElapsedUpdated;
        _session.StateChanged   += OnStateChanged;
    }

    private void OnElapsedUpdated(object? sender, TimeSpan elapsed)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
            ElapsedText = elapsed.ToString(@"hh\:mm\:ss"));
    }

    private void OnStateChanged(object? sender, RecordingState state)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
            IsPaused = state == RecordingState.Paused);
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (_session.State == RecordingState.Paused)
            await _session.ResumeAsync();
        else
            await _session.PauseAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _session.StopAsync();
        await Application.Current.Dispatcher.InvokeAsync(() => _window.Close());
    }

    public void Dispose()
    {
        _session.ElapsedUpdated -= OnElapsedUpdated;
        _session.StateChanged   -= OnStateChanged;
    }
}
