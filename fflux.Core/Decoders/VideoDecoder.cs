using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Helpers;
using fflux.Core.Models;
using fflux.Core.Models.Options;
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

    // 하드웨어 가속 활성화 여부 — ReceiveNextFrame에서 hw→sw 프레임 전송이 필요한지 판별합니다.
    private bool _hwAccelEnabled;

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
    public Task OpenAsync(string filePath, int streamIndex = -1,
                          VideoOpenOptions? options = null,
                          CancellationToken ct = default)
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

        var opts = options ?? VideoOpenOptions.Default;

        // avformat/avcodec I/O + CPU → ThreadPool
        return Task.Run(() => OpenCore(filePath, streamIndex, opts), ct);
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

    private unsafe void OpenCore(string filePath, int preferredStreamIndex, VideoOpenOptions opts)
    {
        ReleaseContext();   // 이전 컨텍스트 정리
        _hwAccelEnabled = false;

        bool isNetwork = IsNetworkUrl(filePath);
        bool isRtsp    = filePath.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);

        // ── 1. avformat_open_input ────────────────────────────────────
        AVFormatContext* fmtCtx   = null;
        AVDictionary*   openOpts = null;

        if (isNetwork)
        {
            // RTSP 전송 프로토콜 (tcp/udp/http)
            if (isRtsp)
                ffmpeg.av_dict_set(&openOpts, "rtsp_transport", opts.RtspTransport, 0);

            // 소켓 I/O 타임아웃 (microseconds 단위)
            // RTSP는 "stimeout", 나머지는 "timeout"
            var timeoutKey  = isRtsp ? "stimeout" : "timeout";
            var timeoutUsec = (opts.TimeoutSeconds * 1_000_000L).ToString();
            ffmpeg.av_dict_set(&openOpts, timeoutKey, timeoutUsec, 0);

            // 스트림 정보 분석 시간/크기를 줄여 빠른 시작을 유도합니다.
            var probeSizeBytes = (opts.ProbeSizeKb * 1024L).ToString();
            var analyzeDurUsec = ((long)(opts.AnalyzeDurationSeconds * 1_000_000)).ToString();
            ffmpeg.av_dict_set(&openOpts, "probesize",       probeSizeBytes, 0);
            ffmpeg.av_dict_set(&openOpts, "analyzeduration", analyzeDurUsec, 0);

            // fflags=nobuffer: 패킷을 버퍼 없이 즉시 디코더로 전달
            if (opts.NoBuffer)
                ffmpeg.av_dict_set(&openOpts, "fflags", "nobuffer", 0);

            // 최대 A/V 디먹싱 지연 (microseconds 단위)
            ffmpeg.av_dict_set(&openOpts, "max_delay",
                (opts.MaxDelayMs * 1000L).ToString(), 0);

            // 소켓 수신 버퍼 (0이면 시스템 기본값 유지)
            if (opts.RecvBufferSizeKb > 0)
                ffmpeg.av_dict_set(&openOpts, "recv_buffer_size",
                    (opts.RecvBufferSizeKb * 1024L).ToString(), 0);

            // RTP 재정렬 큐 (0이면 비활성화)
            if (opts.ReorderQueueSize >= 0)
                ffmpeg.av_dict_set(&openOpts, "reorder_queue_size",
                    opts.ReorderQueueSize.ToString(), 0);

            // 자동 재연결
            if (opts.Reconnect)
            {
                ffmpeg.av_dict_set(&openOpts, "reconnect",          "1", 0);
                ffmpeg.av_dict_set(&openOpts, "reconnect_streamed", "1", 0);
                if (opts.ReconnectDelayMaxSeconds > 0)
                    ffmpeg.av_dict_set(&openOpts, "reconnect_delay_max",
                        opts.ReconnectDelayMaxSeconds.ToString(), 0);
            }
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
            var probeSizeBytes2 = (opts.ProbeSizeKb * 1024L).ToString();
            var analyzeDurUsec2 = ((long)(opts.AnalyzeDurationSeconds * 1_000_000)).ToString();
            ffmpeg.av_dict_set(&findOpts, "probesize",       probeSizeBytes2, 0);
            ffmpeg.av_dict_set(&findOpts, "analyzeduration", analyzeDurUsec2, 0);
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
        // 라이브 스트림: FF_THREAD_FRAME은 N개 스레드 → (N-1)프레임 추가 지연 발생
        //   예) 8코어 → 7프레임 × 40ms = 280ms 추가 지연 → 라이브에 치명적
        //   설정값 1 (기본) 권장. 사용자가 파일처럼 쓰고 싶다면 증가 가능.
        // 파일 재생: 다중 스레드로 처리량 극대화
        if (isNetwork)
        {
            int liveThreads        = Math.Max(1, opts.LiveThreadCount);
            codecCtx->thread_count = liveThreads;
            codecCtx->thread_type  = liveThreads == 1
                ? ffmpeg.FF_THREAD_SLICE   // 단일: 슬라이스 병렬 (지연 없음)
                : ffmpeg.FF_THREAD_FRAME;  // 멀티: 프레임 병렬 (지연 있음)
        }
        else
        {
            int fileThreads        = opts.FileThreadCount > 0
                ? Math.Min(opts.FileThreadCount, 32)
                : Math.Min(Environment.ProcessorCount, 16); // 0 = 자동
            codecCtx->thread_count = fileThreads;
            codecCtx->thread_type  = ffmpeg.FF_THREAD_FRAME;
        }

        // ── skip_loop_filter / skip_frame ────────────────────────────
        // H.264/HEVC 디블록킹 필터 및 프레임 건너뜀으로 CPU 부하를 줄입니다.
        codecCtx->skip_loop_filter = ParseSkipLevel(opts.SkipLoopFilter);
        codecCtx->skip_frame       = ParseSkipLevel(opts.SkipFrame);

        // ── 하드웨어 가속 ────────────────────────────────────────────
        // av_hwdevice_ctx_create 성공 시 GPU 디코딩, 실패 시 소프트웨어 폴백
        if (!string.IsNullOrEmpty(opts.HwAccel) && opts.HwAccel != "none")
            TryEnableHwAccel(codecCtx, opts.HwAccel);

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
        // best_effort_timestamp: pts가 AV_NOPTS_VALUE일 때 FFmpeg이 DTS/duration으로
        // 재구성한 최선 추정값입니다. B-프레임 또는 PTS 누락 컨테이너에서 유효합니다.
        var ts     = TimeSpan.Zero;
        long rawPts = frame->pts != ffmpeg.AV_NOPTS_VALUE
            ? frame->pts
            : frame->best_effort_timestamp;

        if (rawPts != ffmpeg.AV_NOPTS_VALUE && _videoStreamIndex >= 0)
        {
            var tb = fmtCtx->streams[_videoStreamIndex]->time_base;
            if (tb.den > 0)
            {
                double sec = rawPts * tb.num / (double)tb.den;
                // sec >= 0: pts=0은 정상적인 첫 프레임이므로 허용
                if (sec >= 0) ts = TimeSpan.FromSeconds(sec);
            }
        }

        // ── 하드웨어 프레임 → 시스템 메모리 전송 ──────────────────────────
        // 하드웨어 가속 활성 시 frame->hw_frames_ctx != null.
        // sws_scale은 시스템 메모리 픽셀 포맷만 처리하므로 먼저 전송해야 합니다.
        AVFrame* convertSrc = frame;
        AVFrame* swFrame    = null;

        if (_hwAccelEnabled && frame->hw_frames_ctx != null)
        {
            swFrame = ffmpeg.av_frame_alloc();
            if (swFrame != null)
            {
                int hwRet = ffmpeg.av_hwframe_transfer_data(swFrame, frame, 0);
                if (hwRet >= 0)
                {
                    swFrame->pts = frame->pts;  // PTS 복사
                    convertSrc   = swFrame;
                }
                else
                {
                    _logger.LogWarning("HW 프레임 시스템 메모리 전송 실패: {Err}", GetErrorMessage(hwRet));
                    ffmpeg.av_frame_free(&swFrame);
                    swFrame = null;
                    // convertSrc = frame 그대로 → sws_scale이 실패하면 null 반환
                }
            }
        }

        var videoFrame = _converter!.Convert(convertSrc, convertSrc->width, convertSrc->height, ts);

        if (swFrame != null) ffmpeg.av_frame_free(&swFrame);
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

    // ── 하드웨어 가속 헬퍼 (unsafe) ─────────────────────────────────────

    /// <summary>
    /// 지정한 hw 가속 타입으로 <see cref="AVBufferRef"/> 장치 컨텍스트를 생성하여
    /// <paramref name="codecCtx"/>에 바인딩합니다.
    /// 실패하면 경고 로그를 남기고 소프트웨어 디코딩으로 폴백합니다.
    /// </summary>
    private unsafe void TryEnableHwAccel(AVCodecContext* codecCtx, string hwAccelName)
    {
        var hwType = hwAccelName.Trim().ToLowerInvariant() switch
        {
            "auto"    => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, // Windows 기본
            "d3d11va" => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            "dxva2"   => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            "cuda"    => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            "qsv"     => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            "vulkan"  => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
            _         => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
        };

        if (hwType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            _logger.LogWarning("알 수 없는 하드웨어 가속 타입: {Type}", hwAccelName);
            return;
        }

        AVBufferRef* hwDevCtx = null;
        int ret = ffmpeg.av_hwdevice_ctx_create(&hwDevCtx, hwType, null, null, 0);

        if (ret < 0)
        {
            _logger.LogWarning(
                "하드웨어 가속 초기화 실패 ({Type}) — 소프트웨어 디코딩으로 전환합니다. 오류: {Err}",
                hwAccelName, GetErrorMessage(ret));
            return;
        }

        codecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hwDevCtx);
        ffmpeg.av_buffer_unref(&hwDevCtx);
        _hwAccelEnabled = true;

        _logger.LogInformation("하드웨어 가속 활성화: {Type}", hwAccelName);
    }

    /// <summary>설정 문자열을 <see cref="AVDiscard"/> 값으로 변환합니다.</summary>
    private static AVDiscard ParseSkipLevel(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "nonref"  => AVDiscard.AVDISCARD_NONREF,
            "bidir"   => AVDiscard.AVDISCARD_BIDIR,
            "nonkey"  => AVDiscard.AVDISCARD_NONKEY,
            "all"     => AVDiscard.AVDISCARD_ALL,
            _         => AVDiscard.AVDISCARD_DEFAULT,   // "none" 포함
        };

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
