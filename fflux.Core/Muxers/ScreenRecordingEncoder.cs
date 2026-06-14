using fflux.Core.Abstractions;
using fflux.Core.Exceptions;

namespace fflux.Core.Muxers;

/// <summary>
/// BGRA32 캡처 프레임 → YUV420P → mpeg4 → MKV 인코딩.
/// 오디오는 Float32 또는 Int16 PCM을 받아 PCM_S16LE 스트림으로 기록합니다.
/// unsafe 코드를 모두 동기 헬퍼 메서드로 분리하여 CS4004 문제를 회피합니다.
/// </summary>
public sealed class ScreenRecordingEncoder : IScreenRecordingEncoder
{
    private const int SWS_BILINEAR    = 2;
    private const int AVERROR_EAGAIN  = -11;

    private readonly IFFmpegInitializer _init;
    private readonly ILogger<ScreenRecordingEncoder> _logger;

    // nint 핸들 — unsafe 포인터를 안전한 정수로 보관
    private nint _fmtCtx;        // AVFormatContext*
    private nint _videoCodecCtx; // AVCodecContext*  (mpeg4)
    private nint _swsCtx;        // SwsContext*      BGRA→YUV420P
    private nint _yuvFrame;      // AVFrame*         YUV420P 재사용 버퍼
    private nint _encPkt;        // AVPacket*        비디오 패킷 재사용

    private int  _videoStreamIdx = -1;
    private int  _audioStreamIdx = -1;
    private long _videoFramePts;
    private long _audioPts;
    private int  _fps, _videoWidth, _videoHeight;
    private int  _audioSampleRate;
    private bool _headerWritten;
    private bool _disposed;

    // 비디오·오디오를 다른 스레드에서 동시에 기록하므로 muxer 접근을 직렬화
    private readonly object _muxLock = new();

    public bool IsOpen { get; private set; }

    public ScreenRecordingEncoder(IFFmpegInitializer init, ILogger<ScreenRecordingEncoder> logger)
    {
        _init   = init;
        _logger = logger;
    }

    // ── 공개 API ─────────────────────────────────────────────────────

    public void Open(string outputPath, int width, int height, int fps,
                     int audioSampleRate = 0, int audioChannels = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen) throw new InvalidOperationException("인코더가 이미 열려 있습니다.");
        if (!_init.IsInitialized) throw new InvalidOperationException("FFmpeg이 초기화되지 않았습니다.");

        _fps             = fps;
        _videoWidth      = width;
        _videoHeight     = height;
        _videoFramePts   = 0;
        _audioPts        = 0;
        _audioSampleRate = audioSampleRate;
        _headerWritten   = false;

        OpenUnsafe(outputPath, width, height, fps, audioSampleRate, audioChannels);
        IsOpen = true;

        _logger.LogInformation(
            "화면 녹화 인코더 열기: {Path} ({W}×{H} @{Fps}fps, 오디오: {ARate}Hz {ACh}ch)",
            outputPath, width, height, fps, audioSampleRate, audioChannels);
    }

    public void WriteVideoFrame(byte[] bgraData)
    {
        if (!IsOpen) return;
        WriteVideoFrameUnsafe(bgraData);
    }

    public void WriteAudioSamples(byte[] pcmData, int sampleRate, int channels, bool isFloat32)
    {
        if (!IsOpen || _audioStreamIdx < 0) return;
        WriteAudioUnsafe(pcmData, channels, isFloat32);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        FlushAndCloseUnsafe();
        _logger.LogInformation("화면 녹화 인코더 닫기 완료");
    }

    // ── unsafe 헬퍼 ─────────────────────────────────────────────────

    private unsafe void OpenUnsafe(string outputPath, int w, int h, int fps,
                                    int audioRate, int audioCh)
    {
        // 1. MKV 출력 컨텍스트
        AVFormatContext* fmtCtx = null;
        int ret = ffmpeg.avformat_alloc_output_context2(&fmtCtx, null, null, outputPath);
        if (ret < 0 || fmtCtx == null)
            throw new MediaReadException($"출력 컨텍스트 생성 실패: {Err(ret)}", ret);
        _fmtCtx = (nint)fmtCtx;

        // 2. mpeg4 인코더 (LGPL 내장 코덱 — x264/x265 사용 불가)
        var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MPEG4);
        if (codec == null)
            throw new InvalidOperationException("mpeg4 인코더를 찾을 수 없습니다.");

        var codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        if (codecCtx == null)
            throw new InvalidOperationException("AVCodecContext 할당 실패.");
        _videoCodecCtx = (nint)codecCtx;

        codecCtx->width         = w;
        codecCtx->height        = h;
        codecCtx->pix_fmt       = AVPixelFormat.AV_PIX_FMT_YUV420P;
        codecCtx->bit_rate      = 6_000_000L;          // 6 Mbps
        codecCtx->framerate     = new AVRational { num = fps, den = 1 };
        codecCtx->time_base     = new AVRational { num = 1,   den = fps };
        codecCtx->gop_size      = fps;                  // 1초마다 I-프레임
        codecCtx->max_b_frames  = 0;                    // B-프레임 없음 (지연 최소화)

        if ((fmtCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        ret = ffmpeg.avcodec_open2(codecCtx, codec, null);
        if (ret < 0)
            throw new MediaReadException($"mpeg4 인코더 열기 실패: {Err(ret)}", ret);

        // 3. 비디오 스트림
        var videoStream = ffmpeg.avformat_new_stream(fmtCtx, null);
        if (videoStream == null)
            throw new InvalidOperationException("비디오 스트림 추가 실패.");

        ret = ffmpeg.avcodec_parameters_from_context(videoStream->codecpar, codecCtx);
        if (ret < 0)
            throw new MediaReadException($"코덱 파라미터 복사 실패: {Err(ret)}", ret);

        videoStream->time_base = codecCtx->time_base;
        _videoStreamIdx = (int)fmtCtx->nb_streams - 1;

        // 4. PCM_S16LE 오디오 스트림 (인코더 불필요 — 직접 PCM 패킷 기록)
        if (audioRate > 0 && audioCh > 0)
        {
            var audioStream = ffmpeg.avformat_new_stream(fmtCtx, null);
            if (audioStream != null)
            {
                audioStream->codecpar->codec_type  = AVMediaType.AVMEDIA_TYPE_AUDIO;
                audioStream->codecpar->codec_id    = AVCodecID.AV_CODEC_ID_PCM_S16LE;
                audioStream->codecpar->sample_rate = audioRate;
                audioStream->codecpar->format      = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
                audioStream->codecpar->bit_rate    = audioRate * audioCh * 16L;
                audioStream->codecpar->block_align = audioCh * 2;

                AVChannelLayout layout = default;
                ffmpeg.av_channel_layout_default(&layout, audioCh);
                audioStream->codecpar->ch_layout = layout;

                audioStream->time_base = new AVRational { num = 1, den = audioRate };
                _audioStreamIdx = (int)fmtCtx->nb_streams - 1;
            }
        }

        // 5. 출력 파일 열기
        if ((fmtCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            AVIOContext* pb = null;
            ret = ffmpeg.avio_open(&pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
                throw new MediaReadException($"출력 파일 열기 실패: {Err(ret)}", ret);
            fmtCtx->pb = pb;
        }

        // 6. 헤더 기록
        ret = ffmpeg.avformat_write_header(fmtCtx, null);
        if (ret < 0)
            throw new MediaReadException($"MKV 헤더 기록 실패: {Err(ret)}", ret);
        _headerWritten = true;

        // 7. YUV420P 재사용 프레임 버퍼
        var yuvFrame = ffmpeg.av_frame_alloc();
        if (yuvFrame == null)
            throw new InvalidOperationException("YUV 프레임 할당 실패.");
        yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        yuvFrame->width  = w;
        yuvFrame->height = h;
        ret = ffmpeg.av_frame_get_buffer(yuvFrame, 0);
        if (ret < 0)
            throw new MediaReadException($"YUV 프레임 버퍼 할당 실패: {Err(ret)}", ret);
        _yuvFrame = (nint)yuvFrame;

        // 8. SwsContext: BGRA → YUV420P
        var swsCtx = ffmpeg.sws_getContext(
            w, h, AVPixelFormat.AV_PIX_FMT_BGRA,
            w, h, AVPixelFormat.AV_PIX_FMT_YUV420P,
            SWS_BILINEAR, null, null, null);
        if (swsCtx == null)
            throw new InvalidOperationException("BGRA→YUV420P SwsContext 생성 실패.");
        _swsCtx = (nint)swsCtx;

        // 9. 재사용 AVPacket
        var encPkt = ffmpeg.av_packet_alloc();
        if (encPkt == null)
            throw new InvalidOperationException("AVPacket 할당 실패.");
        _encPkt = (nint)encPkt;
    }

    private unsafe void WriteVideoFrameUnsafe(byte[] bgraData)
    {
        var swsCtx   = (SwsContext*)_swsCtx;
        var yuvFrame = (AVFrame*)_yuvFrame;
        var codecCtx = (AVCodecContext*)_videoCodecCtx;
        var fmtCtx   = (AVFormatContext*)_fmtCtx;
        var encPkt   = (AVPacket*)_encPkt;
        var stream   = fmtCtx->streams[_videoStreamIdx];

        // BGRA → YUV420P (PixelFormatConverter와 동일 패턴)
        fixed (byte* bgraPtr = bgraData)
        {
            var srcData     = new byte*[8] { bgraPtr, null, null, null, null, null, null, null };
            var srcLinesize = new int[8]   { _videoWidth * 4, 0, 0, 0, 0, 0, 0, 0 };

            var dstData     = new byte*[8];
            var dstLinesize = new int[8];
            for (uint i = 0; i < 8; i++)
            {
                dstData[i]     = yuvFrame->data[i];
                dstLinesize[i] = yuvFrame->linesize[i];
            }

            ffmpeg.sws_scale(swsCtx, srcData, srcLinesize, 0, _videoHeight, dstData, dstLinesize);
        }

        // av_rescale_q 로 "프레임 번호 / fps" → 실제 codec time_base 단위로 변환
        // mpeg4 코덱이 avcodec_open2 후 time_base 를 바꿀 수 있으므로 직접 rescale 필수
        yuvFrame->pts = ffmpeg.av_rescale_q(
            _videoFramePts++,
            new AVRational { num = 1, den = _fps },
            codecCtx->time_base);

        // 인코딩 → 패킷 수신 루프
        int ret = ffmpeg.avcodec_send_frame(codecCtx, yuvFrame);
        if (ret < 0)
        {
            _logger.LogWarning("avcodec_send_frame 실패: {Err}", Err(ret));
            return;
        }

        while (true)
        {
            ret = ffmpeg.avcodec_receive_packet(codecCtx, encPkt);
            if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF) break;
            if (ret < 0)
            {
                _logger.LogWarning("avcodec_receive_packet 실패: {Err}", Err(ret));
                break;
            }

            encPkt->stream_index = _videoStreamIdx;
            ffmpeg.av_packet_rescale_ts(encPkt, codecCtx->time_base, stream->time_base);

            lock (_muxLock)
                ffmpeg.av_interleaved_write_frame(fmtCtx, encPkt);

            ffmpeg.av_packet_unref(encPkt);
        }
    }

    private unsafe void WriteAudioUnsafe(byte[] pcmData, int channels, bool isFloat32)
    {
        var fmtCtx = (AVFormatContext*)_fmtCtx;

        // Float32 → Int16 변환
        byte[] s16Data;
        int sampleCount;

        if (isFloat32)
        {
            sampleCount = pcmData.Length / 4;
            s16Data = new byte[sampleCount * 2];
            for (int i = 0; i < sampleCount; i++)
            {
                float f = BitConverter.ToSingle(pcmData, i * 4);
                short s = (short)(Math.Clamp(f, -1f, 1f) * short.MaxValue);
                s16Data[i * 2]     = (byte)(s & 0xFF);
                s16Data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
        }
        else
        {
            s16Data = pcmData;
            sampleCount = pcmData.Length / 2;
        }

        int samplesPerChannel = sampleCount / Math.Max(1, channels);

        AVPacket* pkt = null;
        try
        {
            pkt = ffmpeg.av_packet_alloc();
            if (pkt == null) return;

            int ret = ffmpeg.av_new_packet(pkt, s16Data.Length);
            if (ret < 0) return;

            fixed (byte* src = s16Data)
                Buffer.MemoryCopy(src, pkt->data, s16Data.Length, s16Data.Length);

            pkt->stream_index = _audioStreamIdx;
            pkt->pts          = _audioPts;
            pkt->dts          = _audioPts;
            pkt->duration     = samplesPerChannel;
            _audioPts        += samplesPerChannel;

            // MKV muxer changes audioStream->time_base to {1,1000} after avformat_write_header.
            // Without rescaling, sample-unit PTS (48000/sec) is misread as ms (48s/sec of audio).
            var audioStream = fmtCtx->streams[_audioStreamIdx];
            ffmpeg.av_packet_rescale_ts(pkt,
                new AVRational { num = 1, den = _audioSampleRate },
                audioStream->time_base);

            lock (_muxLock)
                ffmpeg.av_interleaved_write_frame(fmtCtx, pkt);
        }
        finally
        {
            if (pkt != null) ffmpeg.av_packet_free(&pkt);
        }
    }

    private unsafe void FlushAndCloseUnsafe()
    {
        try
        {
            if (_videoCodecCtx != 0 && _fmtCtx != 0 && _encPkt != 0)
            {
                var codecCtx = (AVCodecContext*)_videoCodecCtx;
                var fmtCtx   = (AVFormatContext*)_fmtCtx;
                var encPkt   = (AVPacket*)_encPkt;
                var stream   = fmtCtx->streams[_videoStreamIdx];

                // 인코더 플러시 (null 프레임 전송)
                ffmpeg.avcodec_send_frame(codecCtx, null);

                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(codecCtx, encPkt);
                    if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF) break;
                    if (ret < 0) break;

                    encPkt->stream_index = _videoStreamIdx;
                    ffmpeg.av_packet_rescale_ts(encPkt, codecCtx->time_base, stream->time_base);

                    lock (_muxLock)
                        ffmpeg.av_interleaved_write_frame(fmtCtx, encPkt);

                    ffmpeg.av_packet_unref(encPkt);
                }

                if (_headerWritten)
                {
                    lock (_muxLock)
                        ffmpeg.av_write_trailer(fmtCtx);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "인코더 플러시 중 오류");
        }
        finally
        {
            ReleaseUnsafe();
        }
    }

    private unsafe void ReleaseUnsafe()
    {
        _headerWritten = false;

        if (_encPkt != 0)
        {
            var p = (AVPacket*)_encPkt;
            ffmpeg.av_packet_free(&p);
            _encPkt = 0;
        }
        if (_yuvFrame != 0)
        {
            var f = (AVFrame*)_yuvFrame;
            ffmpeg.av_frame_free(&f);
            _yuvFrame = 0;
        }
        if (_swsCtx != 0)
        {
            ffmpeg.sws_freeContext((SwsContext*)_swsCtx);
            _swsCtx = 0;
        }
        if (_videoCodecCtx != 0)
        {
            var ctx = (AVCodecContext*)_videoCodecCtx;
            ffmpeg.avcodec_free_context(&ctx);
            _videoCodecCtx = 0;
        }
        if (_fmtCtx != 0)
        {
            var fmtCtx = (AVFormatContext*)_fmtCtx;
            if (fmtCtx->oformat != null
                && (fmtCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0
                && fmtCtx->pb != null)
            {
                ffmpeg.avio_closep(&fmtCtx->pb);
            }
            ffmpeg.avformat_free_context(fmtCtx);
            _fmtCtx = 0;
        }

        _videoStreamIdx = -1;
        _audioStreamIdx = -1;
    }

    private static unsafe string Err(int code)
    {
        const int bufSize = 256;
        byte* buf = stackalloc byte[bufSize];
        ffmpeg.av_make_error_string(buf, (ulong)bufSize, code);
        return Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0') ?? $"error {code}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsOpen) { IsOpen = false; FlushAndCloseUnsafe(); }
    }
}
