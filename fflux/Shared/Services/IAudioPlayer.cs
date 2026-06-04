namespace fflux.UI.Shared.Services;

/// <summary>
/// PCM float 샘플을 받아 오디오 출력 장치로 재생하는 서비스 인터페이스입니다.
/// </summary>
/// <remarks>
/// 사용 흐름:
/// <code>
/// player.Initialize(sampleRate: 48000, channels: 2);
/// player.Play();
/// player.QueueSamples(audioFrame.Samples);  // 오디오 루프에서 반복 호출
/// player.Stop();
/// </code>
/// </remarks>
public interface IAudioPlayer : IDisposable
{
    /// <summary>현재 재생 중이면 true</summary>
    bool IsPlaying { get; }

    /// <summary>버퍼에 남아있는 재생 대기 시간 (NAudio BufferedWaveProvider 기준)</summary>
    TimeSpan BufferedDuration { get; }

    /// <summary>
    /// 출력 포맷을 설정하고 장치를 초기화합니다.
    /// 이미 초기화된 경우 기존 장치를 해제하고 재초기화합니다.
    /// </summary>
    /// <param name="sampleRate">샘플레이트 (Hz, 예: 48000)</param>
    /// <param name="channels">채널 수 (1=모노, 2=스테레오)</param>
    void Initialize(int sampleRate, int channels);

    /// <summary>
    /// IEEE float (인터리브) 샘플을 재생 버퍼에 추가합니다.
    /// <see cref="AudioFrame.Samples"/>를 직접 전달하면 됩니다.
    /// </summary>
    void QueueSamples(float[] samples);

    /// <summary>재생 시작 또는 재개합니다.</summary>
    void Play();

    /// <summary>재생을 일시 정지합니다. 버퍼는 유지됩니다.</summary>
    void Pause();

    /// <summary>재생을 중지하고 버퍼를 비웁니다.</summary>
    void Stop();

    /// <summary>재생 버퍼를 즉시 비웁니다. 시크 시 오래된 샘플 제거에 사용합니다.</summary>
    void ClearBuffer();

    /// <summary>볼륨과 음소거 상태를 동시에 설정합니다.</summary>
    /// <param name="volume">0.0 (무음) ~ 1.0 (최대)</param>
    /// <param name="muted">true이면 출력 음소거</param>
    void SetVolume(float volume, bool muted);
}
