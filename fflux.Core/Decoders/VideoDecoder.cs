using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Helpers;
using fflux.Core.Models;
using fflux.Core.Models.StreamInfo;

namespace fflux.Core.Decoders;

/// <summary>
/// ffmpeg.autogen을 사용하는 소프트웨어 비디오 디코더입니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>모든 unsafe 포인터 연산은 이 클래스 내부에 격리됩니다.</item>
///   <item>외부에서는 <see cref="IVideoDecoder"/> 인터페이스만 사용하세요.</item>
///   <item>DI 등록: Transient — 재생 세션마다 독립적인 코덱 컨텍스트를 가집니다.</item>
/// </list>
/// </remarks>
public sealed class VideoDecoder : IVideoDecoder
{
    // ── 의존성 ──────────────────────────────────────────────────────

    private readonly IFFmpegInitializer   _initializer;
    private readonly ILogger<VideoDecoder> _logger;

    // ── FFmpeg 핸들 (nint = IntPtr) ──────────────────────────────────
    //
    // 클래스에 `unsafe`를 붙이면 모든 async 메서드도 unsafe 컨텍스트가 되어
    // CS4004(await in unsafe context)가 발생합니다.
    // nint 핸들로 포인터를 저장하고, unsafe 접근이 필요한 곳만 unsafe 메서드로
    // 분리하면 async/await와 공존할 수 있습니다.

    private nint _fmtCtxHandle;    // AVFormatContext*
    private nint _codecCtxHandle;  // AVCodecContext*
    private nint _frameHandle;     // AVFrame*
    private nint _packetHandle;    // AVPacket*

    private int _videoStreamIndex = -1;
    private PixelFormatConverter? _converter;

    // AVERROR(EAGAIN): 디코더에 더 많은 입력이 필요한 상태 (= -11 on all FFmpeg platforms)
    private const int AVERROR_EAGAIN = -11;

    // ── IVideoDecoder 프로퍼티 ───────────────────────────────────────

    /// <inheritdoc/>
    public VideoStreamInfo? StreamInfo { get; private set; }

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public TimeSpan Duration { get; private set; }

    private bool _disposed;

    // ── 생성자 ──────────────────────────────────────────────────────

    public VideoDecoder(
        IFFmpegInitializer    initializer,
        ILogger<VideoDecoder> logger)
    {
        _initializer = initializer;
        _logger      = logger;
    }

    // ── IVideoDecoder 구현 ───────────────────────────────────────────

    /// <inheritdoc/>
    public Task OpenAsync(string filePath, int streamIndex = -1, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initializer.IsInitialized)
            throw new InvalidOperationException(
                "FFmpeg가 초기화되지 않았습니다. " +
                "설정에서 FFmpeg LGPL 바이너리 경로를 지정한 후 저장하세요.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.", filePath);

        // avformat/avcodec I/O + CPU → ThreadPool
        return Task.Run(() => OpenCore(filePath, streamIndex), ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<VideoFrame> DecodeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        // 백그라운드 디코딩과 소비자 사이에 백프레셔 채널 (최대 8 프레임 버퍼)
        var channel = Channel.CreateBounded<VideoFrame>(
            new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait });

        var decodeTask = Task.Run(async () =>
        {
            try   { await DecodeAllAsync(channel.Writer, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { channel.Writer.TryComplete(ex); return; }
            finally { channel.Writer.TryComplete(); }
        }, ct);

        await foreach (var frame in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return frame;

        // 디코딩 태스크의 예외를 소비자 측에서도 받을 수 있도록 재전파
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
    // async 메서드에서는 await를 사용하므로 unsafe 블록/메서드를 직접 쓸 수 없음.
    // 대신, 아래 unsafe 헬퍼를 호출하는 방식으로 분리합니다.
    // ═══════════════════════════════════════════════════════════════════

    // ── 내부: 파일 열기 (unsafe) ─────────────────────────────────────

    private unsafe void OpenCore(string filePath, int preferredStreamIndex)
    {
        ReleaseContext();   // 이전 컨텍스트 정리

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

        // 3. 최적 비디오 스트림 선택
        AVCodec* codec = null;
        int streamIdx = ffmpeg.av_find_best_stream(
            fmtCtx,
            AVMediaType.AVMEDIA_TYPE_VIDEO,
            preferredStreamIndex,
            -1,
            &codec,
            0);

        if (streamIdx < 0)
            throw new MediaReadException("비디오 스트림을 찾을 수 없습니다.");

        _videoStreamIndex = streamIdx;
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

        // 멀티스레드 프레임 디코딩 활성화
        codecCtx->thread_count = Math.Min(Environment.ProcessorCount, 16);
        codecCtx->thread_type  = ffmpeg.FF_THREAD_FRAME;

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

        // 7. 픽셀 포맷 변환기
        _converter = new PixelFormatConverter();

        // 8. 메타데이터 수집
        Duration   = fmtCtx->duration > 0
            ? TimeSpan.FromSeconds(fmtCtx->duration / (double)ffmpeg.AV_TIME_BASE)
            : TimeSpan.Zero;

        StreamInfo = BuildStreamInfo(stream, streamIdx);
        IsOpen     = true;

        _logger.LogInformation(
            "비디오 디코더 열기 완료: {File} — {Codec} {W}×{H} {Fps:F2}fps",
            Path.GetFileName(filePath),
            StreamInfo.CodecName, StreamInfo.Width, StreamInfo.Height, StreamInfo.FrameRate);
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
        _logger.LogDebug("시크 완료: {Position}", position);
    }

    // ── 내부: 패킷 읽기 결과 ─────────────────────────────────────────

    private enum ReadPacketResult { VideoPacket, NonVideoPacket, EndOfFile }

    /// <summary>다음 패킷을 읽고 비디오 스트림 여부 및 EOF를 반환합니다. (unsafe)</summary>
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

        return packet->stream_index == _videoStreamIndex
            ? ReadPacketResult.VideoPacket
            : ReadPacketResult.NonVideoPacket;
    }

    /// <summary>현재 패킷을 디코더에 전송합니다. flush=true이면 null 패킷(플러시). (unsafe)</summary>
    private unsafe void SendPacket(bool flush = false)
    {
        if (_disposed || _codecCtxHandle == 0) return;

        var codecCtx = (AVCodecContext*)_codecCtxHandle;
        var packet   = flush ? null : (AVPacket*)_packetHandle;

        int ret = ffmpeg.avcodec_send_packet(codecCtx, packet);

        if (ret < 0 && ret != AVERROR_EAGAIN)
            _logger.LogWarning("avcodec_send_packet 실패: {Err}", GetErrorMessage(ret));
    }

    /// <summary>현재 패킷 참조를 해제합니다. (unsafe)</summary>
    private unsafe void UnrefPacket()
    {
        if (_packetHandle == 0) return;
        ffmpeg.av_packet_unref((AVPacket*)_packetHandle);
    }

    /// <summary>
    /// 디코더에서 다음 프레임을 수신하고 BGRA VideoFrame으로 변환합니다.
    /// 더 이상 수신할 프레임이 없으면 null을 반환합니다. (unsafe)
    /// </summary>
    private unsafe VideoFrame? ReceiveNextFrame()
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
        if (frame->pts != ffmpeg.AV_NOPTS_VALUE && _videoStreamIndex >= 0)
        {
            var tb = fmtCtx->streams[_videoStreamIndex]->time_base;
            if (tb.den > 0)
            {
                double sec = frame->pts * tb.num / (double)tb.den;
                if (sec > 0) ts = TimeSpan.FromSeconds(sec);
            }
        }

        var videoFrame = _converter!.Convert(frame, frame->width, frame->height, ts);
        ffmpeg.av_frame_unref(frame);

        return videoFrame;
    }

    // ── 내부: 디코드 루프 (async, unsafe 없음) ───────────────────────

    /// <summary>
    /// 파일 끝 또는 취소까지 패킷을 읽어 프레임으로 디코딩하고 채널에 씁니다.
    /// unsafe FFmpeg 호출은 모두 sync 헬퍼를 통해 이루어집니다.
    /// </summary>
    private async Task DecodeAllAsync(ChannelWriter<VideoFrame> writer, CancellationToken ct)
    {
        // ── 메인 루프: 패킷 읽기 ────────────────────────────────────
        while (!ct.IsCancellationRequested)
        {
            var result = ReadNextPacket();  // unsafe 헬퍼 호출 (sync)

            if (result == ReadPacketResult.EndOfFile)
                break;

            if (result == ReadPacketResult.VideoPacket)
            {
                SendPacket(flush: false);                                   // 패킷 전송 (sync)
                await DrainFramesAsync(writer, ct).ConfigureAwait(false);   // 프레임 수신 (async)
            }

            UnrefPacket();  // 패킷 참조 해제 (sync)
        }

        // ── 플러시: 잔류 프레임 수신 ─────────────────────────────────
        if (!ct.IsCancellationRequested)
        {
            SendPacket(flush: true);
            await DrainFramesAsync(writer, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 디코더에서 수신 가능한 모든 프레임을 채널에 씁니다.
    /// 채널이 가득 차면 소비자가 읽을 때까지 비동기로 대기합니다(백프레셔).
    /// </summary>
    private async Task DrainFramesAsync(ChannelWriter<VideoFrame> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = ReceiveNextFrame();   // unsafe 헬퍼 호출 (sync)
            if (frame is null) break;

            // 채널 쓰기 — 가득 차면 await (백프레셔)
            await writer.WriteAsync(frame, ct).ConfigureAwait(false);
        }
    }

    // ── 내부: 스트림 정보 빌더 (unsafe) ─────────────────────────────

    private static unsafe VideoStreamInfo BuildStreamInfo(AVStream* stream, int index)
    {
        var par   = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(par->codec_id);

        double fps = stream->avg_frame_rate.den != 0
            ? stream->avg_frame_rate.num / (double)stream->avg_frame_rate.den
            : 0.0;

        var pixFmt = par->format != (int)AVPixelFormat.AV_PIX_FMT_NONE
            ? ffmpeg.av_get_pix_fmt_name((AVPixelFormat)par->format) ?? "unknown"
            : "unknown";

        var profile = par->profile >= 0
            ? ffmpeg.avcodec_profile_name(par->codec_id, par->profile)
            : null;

        double durationSec = stream->duration > 0 && stream->time_base.den != 0
            ? stream->duration * stream->time_base.num / (double)stream->time_base.den
            : 0;

        return new VideoStreamInfo
        {
            StreamIndex   = index,
            CodecName     = codec != null ? PtrToString(codec->name)      ?? "" : "",
            CodecLongName = codec != null ? PtrToString(codec->long_name) ?? "" : "",
            Profile       = profile,
            Width         = par->width,
            Height        = par->height,
            FrameRate     = fps,
            PixelFormat   = pixFmt,
            BitRate       = par->bit_rate,
            Duration      = durationSec > 0 ? TimeSpan.FromSeconds(durationSec) : TimeSpan.Zero,
        };
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

        _videoStreamIndex = -1;
        StreamInfo        = null;
        IsOpen            = false;
    }
}
