namespace fflux.UI.Modules.ScreenRecorder.Core.Models;

public sealed class AudioDataEventArgs : EventArgs
{
    public byte[] Buffer        { get; }
    public int    BytesRecorded { get; }
    public int    SampleRate    { get; }
    public int    Channels      { get; }
    public bool   IsFloat32     { get; }

    public AudioDataEventArgs(byte[] buffer, int bytesRecorded,
                               int sampleRate, int channels, bool isFloat32)
    {
        Buffer        = buffer;
        BytesRecorded = bytesRecorded;
        SampleRate    = sampleRate;
        Channels      = channels;
        IsFloat32     = isFloat32;
    }
}
