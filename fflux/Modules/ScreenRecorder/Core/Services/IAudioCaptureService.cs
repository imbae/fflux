using fflux.UI.Modules.ScreenRecorder.Core.Models;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

public interface IAudioCaptureService : IDisposable
{
    bool SystemAudioEnabled { get; set; }
    bool MicrophoneEnabled  { get; set; }

    // WASAPI 디바이스의 실제 포맷 (Start() 호출 전에 조회 가능)
    int SampleRate { get; }
    int Channels   { get; }

    event EventHandler<AudioDataEventArgs>? DataAvailable;

    void Start();
    void Stop();
}
