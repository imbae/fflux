namespace fflux.Core.Models;

/// <summary>
/// 디코딩 및 PCM 변환이 완료된 단일 오디오 프레임입니다.
/// 샘플 데이터는 항상 <b>IEEE float (32-bit) 인터리브</b> 형식으로 제공됩니다.
/// </summary>
/// <remarks>
/// 인터리브 레이아웃: (ch0₀, ch1₀, ch0₁, ch1₁, ...) — WPF/NAudio IWaveProvider에 직접 사용 가능.
/// </remarks>
public sealed class AudioFrame
{
    /// <summary>음성 내 이 프레임의 표시 시각(PTS) 기반 타임스탬프</summary>
    public TimeSpan Timestamp { get; }

    /// <summary>
    /// 인터리브 IEEE float 샘플 배열 (ch0, ch1, ch0, ch1, ...).
    /// 크기 = <see cref="SampleCount"/> × <see cref="Channels"/>.
    /// </summary>
    public float[] Samples { get; }

    /// <summary>샘플레이트 (Hz, 예: 48000, 44100)</summary>
    public int SampleRate { get; }

    /// <summary>채널 수 (1=모노, 2=스테레오, ...)</summary>
    public int Channels { get; }

    /// <summary>채널당 샘플 수 (= <see cref="Samples"/>.Length / <see cref="Channels"/>)</summary>
    public int SampleCount { get; }

    public AudioFrame(
        TimeSpan timestamp,
        float[]  samples,
        int      sampleRate,
        int      channels,
        int      sampleCount)
    {
        Timestamp   = timestamp;
        Samples     = samples;
        SampleRate  = sampleRate;
        Channels    = channels;
        SampleCount = sampleCount;
    }
}
