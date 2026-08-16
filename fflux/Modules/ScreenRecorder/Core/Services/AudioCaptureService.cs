using fflux.UI.Modules.ScreenRecorder.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

public sealed class AudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<AudioCaptureService> _logger;

    private WasapiRecorder? _loopback;
    private WaveFormat?     _loopbackFormat;
    private IWaveIn?        _mic;
    private bool _disposed;

    public int SampleRate { get; }
    public int Channels   { get; }

    public bool SystemAudioEnabled { get; set; } = true;
    public bool MicrophoneEnabled  { get; set; } = false;

    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;

        try
        {
            // 루프백 레코더를 잠깐 빌드해 기본 렌더 장치 포맷만 쿼리
            using var probe = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .Build();
            SampleRate = probe.WaveFormat.SampleRate;
            Channels   = probe.WaveFormat.Channels;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WASAPI 포맷 쿼리 실패 — 기본값 사용 (48000Hz 2ch)");
            SampleRate = 48000;
            Channels   = 2;
        }
    }

    public void Start()
    {
        if (SystemAudioEnabled) StartLoopback();
        if (MicrophoneEnabled)  StartMic();
    }

    private void StartLoopback()
    {
        try
        {
            _loopback = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .Build();
            _loopbackFormat      = _loopback.WaveFormat;
            _loopback.DataAvailable += OnLoopbackData;
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "시스템 오디오 캡처 시작 실패");
        }
    }

    private void StartMic()
    {
        try
        {
            var mic = (IWaveIn)Activator.CreateInstance(
                Type.GetType("NAudio.Wave.WasapiCapture, NAudio.Wasapi")
                ?? Type.GetType("NAudio.Wave.WasapiCapture, NAudio")
                ?? throw new InvalidOperationException("WasapiCapture 타입을 찾을 수 없습니다."))!;
            _mic = mic;
            _mic.DataAvailable += OnMicData;
            _mic.StartRecording();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "마이크 캡처 시작 실패");
        }
    }

    // NAudio 3.0.0: DataAvailable 시그니처가 ReadOnlySpan<byte> 기반으로 변경됨
    private void OnLoopbackData(
        ReadOnlySpan<byte>      buffer,
        AudioClientBufferFlags  flags,
        long                    devicePosition,
        long                    qpcPosition)
    {
        if (buffer.Length == 0) return;
        var fmt  = _loopbackFormat!;
        var data = buffer.ToArray();
        DataAvailable?.Invoke(this, new AudioDataEventArgs(
            data, data.Length, fmt.SampleRate, fmt.Channels,
            fmt.Encoding == WaveFormatEncoding.IeeeFloat));
    }

    private void OnMicData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        var fmt  = ((IWaveIn)sender!).WaveFormat;
        var data = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesRecorded);
        DataAvailable?.Invoke(this, new AudioDataEventArgs(
            data, e.BytesRecorded, fmt.SampleRate, fmt.Channels,
            fmt.Encoding == WaveFormatEncoding.IeeeFloat));
    }

    public void Stop()
    {
        try { _loopback?.StopRecording(); } catch { }
        try { _mic?.StopRecording();      } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _loopback?.Dispose();
        _mic?.Dispose();
    }
}
