using System.Windows;

namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

public sealed record RecordingSettings(
    CaptureTarget CaptureTarget,
    Rect? CaptureRegion,
    IntPtr? WindowHandle,
    int Fps,
    string OutputDirectory,
    bool RecordSystemAudio,
    bool RecordMicrophone);
