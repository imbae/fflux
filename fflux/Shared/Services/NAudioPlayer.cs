using NAudio.Wave;

namespace fflux.UI.Shared.Services;

/// <summary>
/// NAudio <c>WaveOutEvent</c> + <c>BufferedWaveProvider</c>를 사용하는 오디오 출력 구현입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>출력 포맷: <b>PCM 16-bit</b> — 모든 Windows 오디오 드라이버가 필수 지원합니다.</item>
///   <item><c>WaveOutEvent</c>는 <c>Play()</c> 호출 시 지연 생성되고 <c>Stop()</c> 시 파괴됩니다.
///         이렇게 해야 <c>waveOutClose</c> 후 재진입 시 드라이버가 반환하는 <c>WaveBadFormat</c>을
///         피할 수 있습니다.</item>
///   <item>이 클래스의 인스턴스는 <c>PlayerViewModel</c>이 수명을 관리합니다 (DI 미등록).</item>
/// </list>
/// </remarks>
internal sealed class NAudioPlayer : IAudioPlayer
{
    private WaveOutEvent?         _waveOut;
    private BufferedWaveProvider? _buffer;
    private float                 _volume = 0.8f;
    private bool                  _isMuted;
    private bool                  _disposed;

    // ── IAudioPlayer 프로퍼티 ────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsPlaying
        => _waveOut?.PlaybackState == PlaybackState.Playing;

    /// <inheritdoc/>
    public TimeSpan BufferedDuration
        => _buffer?.BufferedDuration ?? TimeSpan.Zero;

    // ── IAudioPlayer 구현 ────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize(int sampleRate, int channels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 기존 장치 해제
        DisposeWaveOut();
        _buffer = null;

        // PCM 16-bit: IEEE float(포맷 태그 3)는 드라이버 선택 지원이지만
        // PCM 16-bit(포맷 태그 1)는 모든 Windows 드라이버 필수 지원입니다.
        var fmt = new WaveFormat(sampleRate, 16, channels);

        _buffer = new BufferedWaveProvider(fmt)
        {
            BufferDuration          = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
        };
        // WaveOutEvent는 Play() 호출 시 생성합니다 (지연 생성).
    }

    /// <inheritdoc/>
    public void QueueSamples(float[] samples)
    {
        if (_buffer == null || samples.Length == 0) return;

        // float [-1, 1] → PCM 16-bit signed little-endian
        var shorts = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            shorts[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767f);

        var bytes = new byte[shorts.Length * 2];
        Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    /// <inheritdoc/>
    public void Play()
    {
        if (_buffer == null) return;

        if (_waveOut?.PlaybackState == PlaybackState.Playing)
            return;

        if (_waveOut?.PlaybackState == PlaybackState.Paused)
        {
            // Pause → Play: 기존 핸들 재개 (waveOutClose 호출 없음)
            _waveOut.Play();
            return;
        }

        // null 또는 Stopped: 새 WaveOutEvent로 장치를 재오픈합니다.
        // Stop()이 waveOutClose를 호출했으므로 동일 인스턴스 재사용이 불가합니다.
        DisposeWaveOut();
        _waveOut = new WaveOutEvent { DesiredLatency = 100 };
        _waveOut.Init(_buffer);
        _waveOut.Volume = _isMuted ? 0f : _volume;
        _waveOut.Play();
    }

    /// <inheritdoc/>
    public void Pause()
    {
        if (_waveOut?.PlaybackState == PlaybackState.Playing)
            _waveOut.Pause();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // WaveOutEvent를 파괴해 waveOutClose를 명시적으로 완료합니다.
        // 다음 Play() 호출에서 새 인스턴스를 생성합니다.
        DisposeWaveOut();
        _buffer?.ClearBuffer();
    }

    /// <inheritdoc/>
    public void ClearBuffer() => _buffer?.ClearBuffer();

    /// <inheritdoc/>
    public void SetVolume(float volume, bool muted)
    {
        _volume  = Math.Clamp(volume, 0f, 1f);
        _isMuted = muted;
        if (_waveOut != null)
            _waveOut.Volume = _isMuted ? 0f : _volume;
    }

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeWaveOut();
        _buffer = null;
    }

    // ── 내부 ─────────────────────────────────────────────────────────

    private void DisposeWaveOut()
    {
        if (_waveOut == null) return;
        _waveOut.Stop();
        _waveOut.Dispose();
        _waveOut = null;
    }
}
