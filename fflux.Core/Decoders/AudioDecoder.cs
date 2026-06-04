using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Helpers;
using fflux.Core.Models;
using fflux.Core.Models.StreamInfo;

namespace fflux.Core.Decoders;

/// <summary>
/// ffmpeg.autogen을 사용하는 소프트웨어 오디오 디코더입니다.
/// 디코딩된 오디오를 IEEE float (32-bit) 인터리브 PCM으로 변환합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>모든 unsafe 포인터 연산은 이 클래스 내부에 격리됩니다.</item>
///   <item>외부에서는 <see cref="IAudioDecoder"/> 인터페이스만 사용하세요.</item>
///   <item>DI 등록: Transient — 재생 세션마다 독립적인 코덱 컨텍스트를 가집니다.</item>
/// </list>
/// </remarks>
public sealed class AudioDecoder : IAudioDecoder
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IFFmpegInitializer    _initializer;
    private readonly ILogger<AudioDecoder> _logger;

    // ── FFmpeg 핸들 (nint = IntPtr) ──────────────────────────────────
    //
    // VideoDecoder와 동일한 패턴: nint 핸들로 포인터를 저장하고
    // unsafe 접근이 필요한 곳만 unsafe 메서드로 분리하여
    // async/await와 공존합니다.

    private nint _fmtCtxHandle;    // AVFormatContext*
    private nint _codecCtxHandle;  // AVCodecContext*
    private nint _frameHandle;     // AVFrame*
    private nint _packetHandle;    // AVPacket*

    private int                 _audioStreamIndex = -1;
    private PcmFormatConverter? _converter;

    // AVERROR(EAGAIN): 디코더에 더 많은 입력이 필요한 상태
    private const int AVERROR_EAGAIN = -11;

    // ── IAudioDecoder 프로퍼티 ───────────────────────────────────────

    /// <inheritdoc/>
    public AudioStreamInfo? StreamInfo { get; private set; }

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public TimeSpan Duration { get; private set; }

    private bool _disposed;

    // ── 생성자 ──────────────────────────────────────────────────────

    public AudioDecoder(
        IFFmpegInitializer    initializer,
        ILogger<AudioDecoder> logger)
    {
        _initializer = initializer;
        _logger      = logger;
    }

    // ── IAudioDecoder 구현 ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task OpenAsync(string filePath, int streamIndex = -1, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initializer.IsInitialized)
            throw new InvalidOperationException(
                "FFmpeg가 초기화되지 않았습니다. " +
                "설정에서 FFmpeg LGPL 바이너리 경로를 지정한 후 저장하세요.");

        // 네트워크 URL은 File.Exists() 체크를 건너뜁니다.
        if (!IsNetworkUrl(filePath) && !File.Exists(filePath))
            throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.", filePath);

        return Task.Run(() => OpenCore(filePath, streamIndex), ct);
    }

    private static bool IsNetworkUrl(string source)
        => source.StartsWith("rtsp://",  StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("rtp://",   StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("udp://",   StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("srt://",   StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("rtmp://",  StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioFrame> DecodeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        // 오디오 프레임은 비디오보다 작으므로 버퍼를 16으로 설정
        var channel = Channel.CreateBounded<AudioFrame>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

        var decodeTask = Task.Run(async () =>
        {
            try   { await DecodeAllAsync(channel.Writer, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { channel.Writer.TryComplete(ex); return; }
            finally { channel.Writer.TryComplete(); }
        }, ct);

        await foreach (var frame in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return frame;

        await decodeTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        return Task.Run(() => SeekCore(position), ct);
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        _converter?.Dispose();
        ReleaseContext();

        return ValueTask.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // unsafe 헬퍼: 포인터 접근이 필요한 sync 메서드들
    // ═══════════════════════════════════════════════════════════════════

    // ── 내부: 파일 열기 (unsafe) ─────────────────────────────────────

    private unsafe void OpenCore(string filePath, int preferredStreamIndex)
    {
        ReleaseContext();

        AVFormatContext* fmtCtx = null;

        // 1. avformat_open_input
        int ret = ffmpeg.avformat_open_input(&fmtCtx, filePath, null, null);
        if (ret < 0)
            throw new MediaReadException(
                $"파일을 열 수 없습니다: {filePath}\n{GetErrorMessage(ret)}", ret);

        _fmtCtxHandle = (nint)fmtCtx;

        // 2. avformat_find_stream_info
        ret = ffmpeg.avformat_find_stream_info(fmtCtx, null);
        if (ret < 0)
            throw new MediaReadException(
                $"스트림 정보 추출 실패: {GetErrorMessage(ret)}", ret);

        // 3. 최적 오디오 스트림 선택
        AVCodec* codec = null;
        int streamIdx = ffmpeg.av_find_best_stream(
            fmtCtx,
            AVMediaType.AVMEDIA_TYPE_AUDIO,
            preferredStreamIndex,
            -1,
            &codec,
            0);

        if (streamIdx < 0)
            throw new MediaReadException("오디오 스트림을 찾을 수 없습니다.");

        _audioStreamIndex = streamIdx;
        var stream = fmtCtx->streams[streamIdx];

        // 4. 코덱 컨텍스트 할당 및 파라미터 복사
        var codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        if (codecCtx == null)
            throw new MediaReadException("AVCodecContext 할당 실패.");

        _codecCtxHandle = (nint)codecCtx;

        ret = ffmpeg.avcodec_parameters_to_context(codecCtx, stream->codecpar);
        if (ret < 0)
            throw new MediaReadException(
                $"코덱 파라미터 복사 실패: {GetErrorMessage(ret)}", ret);

        // 5. avcodec_open2
        ret = ffmpeg.avcodec_open2(codecCtx, codec, null);
        if (ret < 0)
            throw new MediaReadException(
                $"코덱 초기화 실패: {GetErrorMessage(ret)}", ret);

        // 6. 재사용 AVFrame / AVPacket 할당
        var frame  = ffmpeg.av_frame_alloc();
        var packet = ffmpeg.av_packet_alloc();

        if (frame == null || packet == null)
            throw new MediaReadException("AVFrame 또는 AVPacket 할당 실패.");

        _frameHandle  = (nint)frame;
        _packetHandle = (nint)packet;

        // 7. PCM 변환기
        _converter = new PcmFormatConverter();

        // 8. 메타데이터 수집
        Duration = fmtCtx->duration > 0
            ? TimeSpan.FromSeconds(fmtCtx->duration / (double)ffmpeg.AV_TIME_BASE)
            : TimeSpan.Zero;

        StreamInfo = BuildStreamInfo(stream, streamIdx);
        IsOpen     = true;

        _logger.LogInformation(
            "오디오 디코더 열기 완료: {File} — {Codec} {Rate}Hz {Ch}ch",
            Path.GetFileName(filePath),
            StreamInfo.CodecName, StreamInfo.SampleRate, StreamInfo.Channels);
    }

    // ── 내부: 시크 (unsafe) ───────────────────────────────────────────

    private unsafe void SeekCore(TimeSpan position)
    {
        var fmtCtx   = (AVFormatContext*)_fmtCtxHandle;
        var codecCtx = (AVCodecContext*)_codecCtxHandle;

        long ts  = (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE);
        int  ret = ffmpeg.av_seek_frame(fmtCtx, -1, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);

        if (ret < 0)
            throw new MediaReadException(
                $"시크 실패 ({position:g}): {GetErrorMessage(ret)}", ret);

        ffmpeg.avcodec_flush_buffers(codecCtx);
        _logger.LogDebug("오디오 시크 완료: {Position}", position);
    }

    // ── 내부: 패킷 읽기 결과 ─────────────────────────────────────────

    private enum ReadPacketResult { AudioPacket, NonAudioPacket, EndOfFile }

    private unsafe ReadPacketResult ReadNextPacket()
    {
        if (_disposed || _fmtCtxHandle == 0 || _packetHandle == 0)
            return ReadPacketResult.EndOfFile;

        var fmtCtx = (AVFormatContext*)_fmtCtxHandle;
        var packet = (AVPacket*)_packetHandle;

        int ret = ffmpeg.av_read_frame(fmtCtx, packet);

        if (ret == ffmpeg.AVERROR_EOF)
            return ReadPacketResult.EndOfFile;

        if (ret < 0)
            throw new MediaReadException($"패킷 읽기 실패: {GetErrorMessage(ret)}", ret);

        return packet->stream_index == _audioStreamIndex
            ? ReadPacketResult.AudioPacket
            : ReadPacketResult.NonAudioPacket;
    }

    private unsafe void SendPacket(bool flush = false)
    {
        if (_disposed || _codecCtxHandle == 0) return;

        var codecCtx = (AVCodecContext*)_codecCtxHandle;
        var packet   = flush ? null : (AVPacket*)_packetHandle;

        int ret = ffmpeg.avcodec_send_packet(codecCtx, packet);

        if (ret < 0 && ret != AVERROR_EAGAIN)
            _logger.LogWarning("avcodec_send_packet 실패: {Err}", GetErrorMessage(ret));
    }

    private unsafe void UnrefPacket()
    {
        if (_packetHandle == 0) return;
        ffmpeg.av_packet_unref((AVPacket*)_packetHandle);
    }

    /// <summary>
    /// 디코더에서 다음 프레임을 수신하고 PCM float AudioFrame으로 변환합니다.
    /// 더 이상 수신할 프레임이 없으면 null을 반환합니다.
    /// </summary>
    private unsafe AudioFrame? ReceiveNextFrame()
    {
        if (_disposed || _codecCtxHandle == 0 || _frameHandle == 0 || _fmtCtxHandle == 0)
            return null;

        var codecCtx = (AVCodecContext*)_codecCtxHandle;
        var frame    = (AVFrame*)_frameHandle;
        var fmtCtx   = (AVFormatContext*)_fmtCtxHandle;

        int ret = ffmpeg.avcodec_receive_frame(codecCtx, frame);

        if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF)
            return null;

        if (ret < 0)
        {
            _logger.LogWarning("avcodec_receive_frame 실패: {Err}", GetErrorMessage(ret));
            return null;
        }

        // PTS → TimeSpan
        var ts = TimeSpan.Zero;
        if (frame->pts != ffmpeg.AV_NOPTS_VALUE && _audioStreamIndex >= 0)
        {
            var tb = fmtCtx->streams[_audioStreamIndex]->time_base;
            if (tb.den > 0)
            {
                double sec = frame->pts * tb.num / (double)tb.den;
                if (sec > 0) ts = TimeSpan.FromSeconds(sec);
            }
        }

        var audioFrame = _converter!.Convert(frame, ts);
        ffmpeg.av_frame_unref(frame);

        return audioFrame;
    }

    // ── 내부: 디코드 루프 (async, unsafe 없음) ───────────────────────

    private async Task DecodeAllAsync(ChannelWriter<AudioFrame> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = ReadNextPacket();

            if (result == ReadPacketResult.EndOfFile)
                break;

            if (result == ReadPacketResult.AudioPacket)
            {
                SendPacket(flush: false);
                await DrainFramesAsync(writer, ct).ConfigureAwait(false);
            }

            UnrefPacket();
        }

        if (!ct.IsCancellationRequested)
        {
            SendPacket(flush: true);
            await DrainFramesAsync(writer, ct).ConfigureAwait(false);
        }
    }

    private async Task DrainFramesAsync(ChannelWriter<AudioFrame> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = ReceiveNextFrame();
            if (frame is null) break;

            await writer.WriteAsync(frame, ct).ConfigureAwait(false);
        }
    }

    // ── 내부: 스트림 정보 빌더 (unsafe) ─────────────────────────────

    private static unsafe AudioStreamInfo BuildStreamInfo(AVStream* stream, int index)
    {
        var par   = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(par->codec_id);

        const int bufSize = 64;
        var buf = stackalloc byte[bufSize];
        string channelLayout = "unknown";
        if (ffmpeg.av_channel_layout_describe(&par->ch_layout, buf, (ulong)bufSize) >= 0)
            channelLayout = Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0') ?? "unknown";

        var sampleFmt = par->format >= 0
            ? ffmpeg.av_get_sample_fmt_name((AVSampleFormat)par->format) ?? "unknown"
            : "unknown";

        return new AudioStreamInfo
        {
            StreamIndex   = index,
            CodecName     = codec != null ? PtrToString(codec->name)      ?? string.Empty : string.Empty,
            CodecLongName = codec != null ? PtrToString(codec->long_name) ?? string.Empty : string.Empty,
            SampleRate    = par->sample_rate,
            Channels      = par->ch_layout.nb_channels,
            ChannelLayout = channelLayout,
            BitRate       = par->bit_rate,
            SampleFormat  = sampleFmt,
            Duration      = CalcDuration(stream),
        };
    }

    private static unsafe TimeSpan CalcDuration(AVStream* stream)
    {
        if (stream->duration <= 0 || stream->time_base.den == 0)
            return TimeSpan.Zero;

        double seconds = stream->duration
            * stream->time_base.num
            / (double)stream->time_base.den;

        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    // ── 유틸리티 ─────────────────────────────────────────────────────

    private static unsafe string? PtrToString(byte* ptr)
        => ptr == null ? null : Marshal.PtrToStringUTF8((IntPtr)ptr);

    private static unsafe string GetErrorMessage(int errorCode)
    {
        const int bufSize = 256;
        var buf = stackalloc byte[bufSize];
        ffmpeg.av_make_error_string(buf, (ulong)bufSize, errorCode);
        return Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0')
               ?? $"FFmpeg error {errorCode}";
    }

    // ── 리소스 정리 (unsafe) ──────────────────────────────────────────

    private unsafe void ReleaseContext()
    {
        if (_frameHandle != 0)
        {
            var p = (AVFrame*)_frameHandle;
            ffmpeg.av_frame_free(&p);
            _frameHandle = 0;
        }

        if (_packetHandle != 0)
        {
            var p = (AVPacket*)_packetHandle;
            ffmpeg.av_packet_free(&p);
            _packetHandle = 0;
        }

        if (_codecCtxHandle != 0)
        {
            var p = (AVCodecContext*)_codecCtxHandle;
            ffmpeg.avcodec_free_context(&p);
            _codecCtxHandle = 0;
        }

        if (_fmtCtxHandle != 0)
        {
            var p = (AVFormatContext*)_fmtCtxHandle;
            ffmpeg.avformat_close_input(&p);
            _fmtCtxHandle = 0;
        }

        _audioStreamIndex = -1;
        StreamInfo        = null;
        IsOpen            = false;
    }
}
