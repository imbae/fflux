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

        // 네트워크 URL은 File.Exists() 체크를 건너뜁니다.
        // (rtsp://, rtp://, udp://, srt://, rtmp://, http://, https://)
        if (!IsNetworkUrl(filePath) && !File.Exists(filePath))
            throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.", filePath);

        // avformat/avcodec I/O + CPU → ThreadPool
        return Task.Run(() => OpenCore(filePath, streamIndex), ct);
    }

    /// <summary>URL 스킴을 보고 네트워크 소스 여부를 판별합니다.</summary>
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
    public async IAsyncEnumerable<VideoFrame> DecodeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        // ── 디코드 채널 ────────────────────────────────────────────────
        // 라이브/파일 모두 Wait 모드: 소비자(ViewModel)가 PTS 속도로 소비하므로
        // 디코더는 8프레임 선행 후 자연스럽게 백프레셔를 받습니다.
        // DropOldest를 사용하면 네트워크 버스트 시 프레임이 유실되어 시각적 점프가 발생합니다.
        //
        // 용량 8프레임:
        //  - 라이브 25fps → 320ms 지터 버퍼
        //  - 라이브 30fps → 267ms 지터 버퍼
        //  - 파일: PTS 타이밍 백프레셔 완충
        bool isLive = Duration == TimeSpan.Zero;
        var channel = Channel.CreateBounded<VideoFrame>(
            new BoundedChannelOptions(8)
            {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });

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

    // ── SeekAndDecodeAtAsync / DecodeNextFrameAfterAsync ─────────────

    /// <inheritdoc/>
    public Task<VideoFrame?> DecodeNextFrameAfterAsync(TimeSpan currentPosition, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        return Task.Run(() => DecodeNextFrameAfter(currentPosition), ct);
    }

    /// <summary>
    /// backward seek 후 <paramref name="currentPosition"/>보다 큰 PTS를 가진 첫 프레임을 반환합니다.
    /// </summary>
    private VideoFrame? DecodeNextFrameAfter(TimeSpan currentPosition)
    {
        SeekCore(currentPosition); // 현재 위치 이전 키프레임으로 이동

        const int maxFrames = 2000;
        for (int i = 0; i < maxFrames && !_disposed; i++)
        {
            var frame = DecodeOneFrame();
            if (frame is null) break;

            if (frame.Timestamp > currentPosition)
                return frame; // 현재 위치 이후 첫 프레임
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<VideoFrame?> SeekAndDecodeAtAsync(TimeSpan target, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsOpen)
            throw new InvalidOperationException(
                "디코더가 열려 있지 않습니다. OpenAsync()를 먼저 호출하세요.");

        return Task.Run(() => SeekAndDecodeAt(target), ct);
    }

    /// <summary>
    /// backward seek 후 목표 PTS에 가장 가까운(≤ target) 프레임을 찾아 반환합니다.
    /// seek → keyframe K (K.pts ≤ target) → K 이후 프레임들을 디코딩하며 target에 도달.
    /// </summary>
    private VideoFrame? SeekAndDecodeAt(TimeSpan target)
    {
        SeekCore(target); // backward seek → 목표 이전 키프레임으로 이동

        VideoFrame? best  = null;
        const int maxFrames = 2000; // 약 ~60fps × 30초 GOP 상한

        for (int i = 0; i < maxFrames && !_disposed; i++)
        {
            var frame = DecodeOneFrame();
            if (frame is null) break;

            if (frame.Timestamp > target)
                break; // 목표를 지나침 — best(직전 프레임)가 정답

            best = frame; // 목표 이하의 프레임을 계속 갱신

            // 타임스탬프가 정확히 일치하면 더 볼 필요 없음
            if (Math.Abs((frame.Timestamp - target).TotalMilliseconds) < 1.0)
                break;
        }

        return best;
    }

    /// <summary>
    /// seek 없이 디코더 현재 위치에서 다음 프레임 하나를 읽어 반환합니다.
    /// <see cref="ReceiveNextFrame"/>을 먼저 시도하고, 버퍼가 비어 있으면 패킷을 읽습니다.
    /// </summary>
    private VideoFrame? DecodeOneFrame()
    {
        // 코덱 내부 버퍼에 남은 프레임 먼저 소비
        var frame = ReceiveNextFrame();
        if (frame is not null) return frame;

        const int maxPackets = 1000;
        for (int i = 0; i < maxPackets && !_disposed; i++)
        {
            var result = ReadNextPacket();

            if (result == ReadPacketResult.EndOfFile)
                return null;

            if (result == ReadPacketResult.VideoPacket)
            {
                SendPacket(flush: false);
                UnrefPacket();
                frame = ReceiveNextFrame();
                if (frame is not null) return frame;
            }
            else
            {
                UnrefPacket(); // 오디오/자막 등 비디오 외 패킷 무시
            }
        }

        return null;
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

        bool isNetwork = IsNetworkUrl(filePath);

        // ── 1. avformat_open_input ────────────────────────────────────
        AVFormatContext* fmtCtx   = null;
        AVDictionary*   openOpts = null;

        if (isNetwork)
        {
            // RTSP: UDP보다 TCP가 안정적 (방화벽/NAT 환경에서도 통과 우수)
            if (filePath.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
                ffmpeg.av_dict_set(&openOpts, "rtsp_transport", "tcp", 0);

            // 소켓 I/O 타임아웃: 10초 (microseconds 단위)
            // RTSP는 "stimeout", 나머지는 "timeout"
            var timeoutKey = filePath.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                ? "stimeout" : "timeout";
            ffmpeg.av_dict_set(&openOpts, timeoutKey, "10000000", 0);

            // 스트림 정보 분석 시간/크기를 줄여 빠른 시작을 유도합니다.
            // (라이브 스트림은 duration 파악이 불가능하므로 짧게 설정)
            ffmpeg.av_dict_set(&openOpts, "probesize",       "500000", 0);  // 500 KB
            ffmpeg.av_dict_set(&openOpts, "analyzeduration", "1000000", 0); // 1 초

            // 입력 데이터를 내부 버퍼에 쌓지 않고 즉시 디코더로 전달합니다.
            // 이 옵션 없이는 FFmpeg이 패킷을 모아서 버스트로 방출 → 버벅거림 원인
            ffmpeg.av_dict_set(&openOpts, "fflags", "nobuffer", 0);

            // 최대 A/V 디먹싱 지연: 0.5초로 제한 (기본값 700ms 이상)
            ffmpeg.av_dict_set(&openOpts, "max_delay", "500000", 0); // microseconds
        }

        int ret = ffmpeg.avformat_open_input(&fmtCtx, filePath, null, &openOpts);
        // 미사용 옵션 키가 있으면 dict에 남으므로 항상 해제
        if (openOpts != null) ffmpeg.av_dict_free(&openOpts);

        if (ret < 0)
            throw new MediaReadException(
                $"소스를 열 수 없습니다: {filePath}\n{GetErrorMessage(ret)}", ret);

        _fmtCtxHandle = (nint)fmtCtx;

        // ── 2. avformat_find_stream_info ─────────────────────────────
        // 라이브 스트림은 스트림 정보 분석을 빠르게 끝내기 위해
        // analyzeduration / probesize를 여기서도 제한합니다.
        AVDictionary* findOpts = null;
        if (isNetwork)
        {
            ffmpeg.av_dict_set(&findOpts, "probesize",       "500000", 0);
            ffmpeg.av_dict_set(&findOpts, "analyzeduration", "1000000", 0);
        }

        ret = ffmpeg.avformat_find_stream_info(fmtCtx, findOpts != null ? &findOpts : null);
        if (findOpts != null) ffmpeg.av_dict_free(&findOpts);

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

        // ── 스레드 설정 ──────────────────────────────────────────────────
        // 라이브 스트림: 단일 스레드 디코딩
        //   FF_THREAD_FRAME은 N개 스레드 사용 시 최대 (N-1)프레임의 디코딩 지연을 유발합니다.
        //   예) 8코어 → 7프레임 × 40ms = 280ms 추가 지연 → 라이브에 치명적
        // 파일 재생: 다중 스레드로 처리량 극대화
        if (isNetwork)
        {
            codecCtx->thread_count = 1;
            codecCtx->thread_type  = ffmpeg.FF_THREAD_SLICE; // thread_count=1이면 무관하나 명시
        }
        else
        {
            codecCtx->thread_count = Math.Min(Environment.ProcessorCount, 16);
            codecCtx->thread_type  = ffmpeg.FF_THREAD_FRAME;
        }

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
