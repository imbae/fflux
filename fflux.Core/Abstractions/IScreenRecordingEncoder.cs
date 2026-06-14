namespace fflux.Core.Abstractions;

/// <summary>
/// BGRA32 캡처 프레임을 받아 MKV 파일로 인코딩하는 화면 녹화 인코더입니다.
/// 비디오: mpeg4 (LGPL 내장 인코더), 오디오: PCM S16LE (무손실, 인코더 불필요).
/// </summary>
public interface IScreenRecordingEncoder : IDisposable
{
    bool IsOpen { get; }

    /// <summary>출력 파일을 열고 인코더/muxer를 초기화합니다.</summary>
    void Open(string outputPath, int width, int height, int fps,
              int audioSampleRate = 0, int audioChannels = 0);

    /// <summary>BGRA32 raw 픽셀 데이터 한 프레임을 인코딩·기록합니다.</summary>
    void WriteVideoFrame(byte[] bgraData);

    /// <summary>
    /// PCM 오디오 데이터를 기록합니다.
    /// isFloat32=true 이면 Float32 → Int16 변환 후 PCM_S16LE로 기록합니다.
    /// </summary>
    void WriteAudioSamples(byte[] pcmData, int sampleRate, int channels, bool isFloat32);

    /// <summary>인코더를 플러시하고 파일 트레일러를 기록한 뒤 닫습니다.</summary>
    void Close();
}
