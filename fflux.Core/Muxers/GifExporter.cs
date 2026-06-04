using fflux.Core.Abstractions;
using fflux.Core.Exceptions;

namespace fflux.Core.Muxers;

/// <summary>
/// ffmpeg.autogen을 사용하여 미디어 구간을 GIF 애니메이션으로 내보냅니다.
/// <para>
/// 파이프라인:
/// <list type="number">
///   <item>avformat_open_input → 비디오 스트림 디코딩</item>
///   <item>sws_scale → AV_PIX_FMT_RGB8 변환 (3R+3G+2B 팩킹, sws_scale 직접 지원)</item>
///   <item>AV_CODEC_ID_GIF 인코더 → 출력 GIF</item>
/// </list>
/// LGPL 안전: GIF 인코더는 FFmpeg 내장이며 GPL 코덱을 링킹하지 않습니다.
/// </para>
/// <para>
/// 참고: AV_PIX_FMT_PAL8은 libswscale이 대부분의 소스 포맷에서 직접 변환을 지원하지 않습니다.
/// RGB8(3-3-2 비트 팩킹, 256가지 색상)를 사용합니다.
/// </para>
/// </summary>
public sealed class GifExporter : IGifExporter
{
    private readonly IFFmpegInitializer _initializer;
    private readonly ILogger<GifExporter> _logger;

    // sws_scale 옵션 상수 (ffmpeg.autogen 8.1.0에서 SWS_BILINEAR 미노출)
    private const int SWS_BILINEAR = 2;

    // AVERROR(EAGAIN) = -11 (모든 FFmpeg 플랫폼 공통)
    private const int AVERROR_EAGAIN = -11;

    public GifExporter(IFFmpegInitializer initializer, ILogger<GifExporter> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task ExportAsync(
        string sourcePath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan duration,
        int maxWidth = 480,
        double targetFps = 10.0,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!_initializer.IsInitialized)
            throw new InvalidOperationException("FFmpeg가 초기화되지 않았습니다.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("소스 파일을 찾을 수 없습니다.", sourcePath);

        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "구간 길이는 0보다 커야 합니다.");

        return Task.Run(
            () => ExportCore(sourcePath, outputPath, startTime, duration, maxWidth, targetFps, progress, ct),
            ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // 핵심 인코딩 루프 (ThreadPool, unsafe 직접 사용)
    // ═══════════════════════════════════════════════════════════════

    private unsafe void ExportCore(
        string sourcePath, string outputPath,
        TimeSpan startTime, TimeSpan duration,
        int maxWidth, double targetFps,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "GIF 내보내기 시작: {Src} → {Dst} [{Start:g} + {Dur:g}] {W}px @{Fps:F1}fps",
            Path.GetFileName(sourcePath), Path.GetFileName(outputPath),
            startTime, duration, maxWidth, targetFps);

        // ── 1. 입력 열기 ──────────────────────────────────────────────
        AVFormatContext* inCtx = null;
        int ret = ffmpeg.avformat_open_input(&inCtx, sourcePath, null, null);
        if (ret < 0)
            throw new MediaReadException($"GIF 내보내기: 입력 열기 실패 {GetErrorMessage(ret)}", ret);

        try
        {
            ret = ffmpeg.avformat_find_stream_info(inCtx, null);
            if (ret < 0)
                throw new MediaReadException($"스트림 정보 추출 실패: {GetErrorMessage(ret)}", ret);

            // ── 2. 비디오 스트림 찾기 ──────────────────────────────────
            AVCodec* decoder = null;
            int vidIdx = ffmpeg.av_find_best_stream(
                inCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
            if (vidIdx < 0 || decoder == null)
                throw new MediaReadException("비디오 스트림을 찾을 수 없습니다.");

            var inStream = inCtx->streams[vidIdx];

            // ── 3. 디코더 컨텍스트 ────────────────────────────────────
            var decCtx = ffmpeg.avcodec_alloc_context3(decoder);
            if (decCtx == null) throw new MediaReadException("디코더 컨텍스트 할당 실패.");

            try
            {
                ret = ffmpeg.avcodec_parameters_to_context(decCtx, inStream->codecpar);
                if (ret < 0)
                    throw new MediaReadException($"코덱 파라미터 복사 실패: {GetErrorMessage(ret)}", ret);

                decCtx->thread_count = Math.Min(Environment.ProcessorCount, 8);
                decCtx->thread_type = ffmpeg.FF_THREAD_FRAME;

                ret = ffmpeg.avcodec_open2(decCtx, decoder, null);
                if (ret < 0)
                    throw new MediaReadException($"디코더 초기화 실패: {GetErrorMessage(ret)}", ret);

                // ── 4. 출력 크기 계산 (비율 유지, 짝수 보정) ────────────
                int srcW = decCtx->width;
                int srcH = decCtx->height;
                int dstW, dstH;

                if (maxWidth > 0 && srcW > maxWidth)
                {
                    dstW = maxWidth % 2 == 0 ? maxWidth : maxWidth - 1;
                    dstH = (int)Math.Round((double)srcH * dstW / srcW);
                    if (dstH % 2 != 0) dstH++;
                }
                else
                {
                    dstW = srcW % 2 == 0 ? srcW : srcW - 1;
                    dstH = srcH % 2 == 0 ? srcH : srcH - 1;
                }

                if (dstW <= 0) dstW = 2;
                if (dstH <= 0) dstH = 2;

                // ── 5. sws 컨텍스트 ─────────────────────────────────────
                // AV_PIX_FMT_PAL8는 libswscale에서 YUV 등 대부분의 소스 포맷으로부터
                // 직접 변환을 지원하지 않습니다. AV_PIX_FMT_RGB8(3-3-2 비트팩 8비트 RGB)를
                // 사용합니다. GIF 인코더가 RGB8을 지원합니다.
                const AVPixelFormat gifPixFmt = AVPixelFormat.AV_PIX_FMT_RGB8;

                var srcPixFmt = (AVPixelFormat)decCtx->pix_fmt;
                var swsCtx = ffmpeg.sws_getContext(
                    srcW, srcH, srcPixFmt,
                    dstW, dstH, gifPixFmt,
                    SWS_BILINEAR, null, null, null);

                if (swsCtx == null)
                    throw new MediaReadException(
                        $"sws_getContext 실패 (소스 픽셀포맷: {srcPixFmt} → RGB8). " +
                        $"소스 픽셀포맷이 sws_scale에서 지원되지 않을 수 있습니다.");

                try
                {
                    // ── 6. GIF 인코더 + 출력 컨텍스트 ─────────────────────
                    var gifCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_GIF);
                    if (gifCodec == null)
                        throw new MediaReadException("GIF 인코더를 찾을 수 없습니다.");

                    var encCtx = ffmpeg.avcodec_alloc_context3(gifCodec);
                    if (encCtx == null)
                        throw new MediaReadException("GIF 인코더 컨텍스트 할당 실패.");

                    try
                    {
                        double fps = targetFps > 0 ? targetFps : (
                            inStream->avg_frame_rate.den != 0
                                ? inStream->avg_frame_rate.num / (double)inStream->avg_frame_rate.den
                                : 25.0);
                        if (fps <= 0) fps = 10.0;

                        // GIF 타임베이스: 100분의 1초 (centiseconds)
                        encCtx->width     = dstW;
                        encCtx->height    = dstH;
                        encCtx->pix_fmt   = gifPixFmt;
                        encCtx->time_base = new AVRational { num = 1, den = 100 };

                        ret = ffmpeg.avcodec_open2(encCtx, gifCodec, null);
                        if (ret < 0)
                            throw new MediaReadException(
                                $"GIF 인코더 열기 실패: {GetErrorMessage(ret)}", ret);

                        // 출력 AVFormatContext (gif 포맷)
                        AVFormatContext* outCtx = null;
                        ret = ffmpeg.avformat_alloc_output_context2(&outCtx, null, "gif", outputPath);
                        if (ret < 0 || outCtx == null)
                            throw new MediaReadException(
                                $"GIF 출력 컨텍스트 할당 실패: {GetErrorMessage(ret)}", ret);

                        try
                        {
                            var outStream = ffmpeg.avformat_new_stream(outCtx, null);
                            if (outStream == null)
                                throw new MediaReadException("GIF 출력 스트림 추가 실패.");

                            outStream->time_base = encCtx->time_base;

                            ret = ffmpeg.avcodec_parameters_from_context(outStream->codecpar, encCtx);
                            if (ret < 0)
                                throw new MediaReadException(
                                    $"GIF 코덱 파라미터 설정 실패: {GetErrorMessage(ret)}", ret);

                            // 출력 파일 열기
                            AVIOContext* pb = null;
                            ret = ffmpeg.avio_open(&pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
                            if (ret < 0)
                                throw new MediaReadException(
                                    $"GIF 출력 파일 열기 실패: {GetErrorMessage(ret)}", ret);
                            outCtx->pb = pb;

                            ret = ffmpeg.avformat_write_header(outCtx, null);
                            if (ret < 0)
                                throw new MediaReadException(
                                    $"GIF 헤더 기록 실패: {GetErrorMessage(ret)}", ret);

                            // ── 7. seek ──────────────────────────────────────
                            if (startTime > TimeSpan.Zero)
                            {
                                long seekTs = (long)(startTime.TotalSeconds * ffmpeg.AV_TIME_BASE);
                                ffmpeg.av_seek_frame(inCtx, -1, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                                ffmpeg.avcodec_flush_buffers(decCtx);
                            }

                            // ── 8. 디코딩 → sws_scale → 인코딩 루프 ─────────
                            var srcFrame = ffmpeg.av_frame_alloc();
                            var dstFrame = ffmpeg.av_frame_alloc();
                            var encPkt   = ffmpeg.av_packet_alloc();

                            if (srcFrame == null || dstFrame == null || encPkt == null)
                                throw new MediaReadException("AVFrame/AVPacket 할당 실패.");

                            try
                            {
                                // dstFrame 버퍼 할당
                                dstFrame->format = (int)gifPixFmt;
                                dstFrame->width  = dstW;
                                dstFrame->height = dstH;
                                ret = ffmpeg.av_frame_get_buffer(dstFrame, 0);
                                if (ret < 0)
                                    throw new MediaReadException(
                                        $"GIF 프레임 버퍼 할당 실패: {GetErrorMessage(ret)}", ret);

                                var inPkt      = ffmpeg.av_packet_alloc();
                                if (inPkt == null)
                                    throw new MediaReadException("입력 AVPacket 할당 실패.");

                                var endTime       = startTime + duration;
                                var frameInterval = TimeSpan.FromSeconds(1.0 / fps);
                                var nextFrameTime = startTime;
                                long gifPts       = 0;
                                long gifFrameDur  = (long)Math.Round(100.0 / fps); // centiseconds
                                if (gifFrameDur < 1) gifFrameDur = 1;
                                double totalSec = duration.TotalSeconds;

                                try
                                {
                                    while (!ct.IsCancellationRequested)
                                    {
                                        ret = ffmpeg.av_read_frame(inCtx, inPkt);

                                        if (ret == ffmpeg.AVERROR_EOF)
                                        {
                                            FlushDecodeToGif(decCtx, srcFrame, dstFrame, encCtx,
                                                outCtx, encPkt, swsCtx, inStream,
                                                startTime, endTime, frameInterval,
                                                ref nextFrameTime, ref gifPts, gifFrameDur,
                                                totalSec, progress, outStream);
                                            break;
                                        }

                                        if (ret < 0)
                                        {
                                            _logger.LogWarning("GIF: 패킷 읽기 오류 {Ret}", ret);
                                            break;
                                        }

                                        if (inPkt->stream_index != vidIdx)
                                        {
                                            ffmpeg.av_packet_unref(inPkt);
                                            continue;
                                        }

                                        // 구간 초과 체크
                                        if (inPkt->pts != ffmpeg.AV_NOPTS_VALUE)
                                        {
                                            double pktSec = inPkt->pts
                                                * inStream->time_base.num
                                                / (double)inStream->time_base.den;

                                            if (pktSec >= endTime.TotalSeconds)
                                            {
                                                ffmpeg.av_packet_unref(inPkt);
                                                FlushDecodeToGif(decCtx, srcFrame, dstFrame, encCtx,
                                                    outCtx, encPkt, swsCtx, inStream,
                                                    startTime, endTime, frameInterval,
                                                    ref nextFrameTime, ref gifPts, gifFrameDur,
                                                    totalSec, progress, outStream);
                                                break;
                                            }
                                        }

                                        ret = ffmpeg.avcodec_send_packet(decCtx, inPkt);
                                        ffmpeg.av_packet_unref(inPkt);
                                        if (ret < 0) continue;

                                        FlushDecodeToGif(decCtx, srcFrame, dstFrame, encCtx,
                                            outCtx, encPkt, swsCtx, inStream,
                                            startTime, endTime, frameInterval,
                                            ref nextFrameTime, ref gifPts, gifFrameDur,
                                            totalSec, progress, outStream);
                                    }
                                }
                                finally
                                {
                                    ffmpeg.av_packet_free(&inPkt);
                                }

                                // ── 9. 인코더 플러시 ──────────────────────────
                                FlushGifEncoder(encCtx, outCtx, encPkt, outStream);
                            }
                            finally
                            {
                                ffmpeg.av_frame_free(&srcFrame);
                                ffmpeg.av_frame_free(&dstFrame);
                                ffmpeg.av_packet_free(&encPkt);
                            }

                            ffmpeg.av_write_trailer(outCtx);
                            _logger.LogInformation("GIF 내보내기 완료: {Path}", outputPath);
                        }
                        finally
                        {
                            if (outCtx != null)
                            {
                                if (outCtx->pb != null)
                                    ffmpeg.avio_closep(&outCtx->pb);
                                ffmpeg.avformat_free_context(outCtx);
                            }
                        }
                    }
                    finally
                    {
                        ffmpeg.avcodec_free_context(&encCtx);
                    }
                }
                finally
                {
                    ffmpeg.sws_freeContext(swsCtx);
                }
            }
            finally
            {
                ffmpeg.avcodec_free_context(&decCtx);
            }
        }
        finally
        {
            ffmpeg.avformat_close_input(&inCtx);
        }

        progress?.Report(1.0);
    }

    // ── 디코더 드레인 + GIF 인코딩 ────────────────────────────────────

    private static unsafe void FlushDecodeToGif(
        AVCodecContext* decCtx, AVFrame* srcFrame, AVFrame* dstFrame,
        AVCodecContext* encCtx, AVFormatContext* outCtx, AVPacket* encPkt,
        SwsContext* swsCtx, AVStream* inStream,
        TimeSpan startTime, TimeSpan endTime, TimeSpan frameInterval,
        ref TimeSpan nextFrameTime, ref long gifPts, long gifFrameDur,
        double totalSec, IProgress<double>? progress, AVStream* outStream)
    {
        while (true)
        {
            int ret = ffmpeg.avcodec_receive_frame(decCtx, srcFrame);
            if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF) break;
            if (ret < 0) break;

            // PTS → 절대 시간 계산
            long rawPts = srcFrame->pts != ffmpeg.AV_NOPTS_VALUE
                ? srcFrame->pts : srcFrame->best_effort_timestamp;

            double frameSec = rawPts != ffmpeg.AV_NOPTS_VALUE
                ? rawPts * inStream->time_base.num / (double)inStream->time_base.den
                : 0;

            var frameTime = TimeSpan.FromSeconds(frameSec);

            // 시작 이전 프레임 건너뜀
            if (frameTime < startTime)
            {
                ffmpeg.av_frame_unref(srcFrame);
                continue;
            }

            // 종료 이후 프레임 건너뜀
            if (frameTime >= endTime)
            {
                ffmpeg.av_frame_unref(srcFrame);
                break;
            }

            // FPS 제한: nextFrameTime 이전 프레임은 건너뜀
            if (frameTime < nextFrameTime)
            {
                ffmpeg.av_frame_unref(srcFrame);
                continue;
            }

            nextFrameTime = frameTime + frameInterval;

            // ── sws_scale: srcFrame → dstFrame (RGB8) ────────────────
            // ffmpeg.autogen 8.1.0: byte_ptrArray8 / int_array8 사용
            var srcData    = new byte_ptrArray8();
            var srcStrides = new int_array8();
            for (uint i = 0; i < 8; i++)
            {
                srcData[i]    = srcFrame->data[i];
                srcStrides[i] = srcFrame->linesize[i];
            }

            // ── dstFrame 쓰기 가능 보장 (av_frame_make_writable) ──────
            // avcodec_send_frame은 내부적으로 dstFrame에 대한 ref를 추가합니다.
            // 다음 sws_scale 호출 전에 make_writable을 호출하지 않으면
            // 인코더가 아직 참조 중인 버퍼를 덮어쓰게 되어 모든 프레임이
            // 동일한 이미지로 고정됩니다 (움짤이 움직이지 않는 버그).
            int writable = ffmpeg.av_frame_make_writable(dstFrame);
            if (writable < 0)
            {
                ffmpeg.av_frame_unref(srcFrame);
                continue;
            }

            // make_writable 후 포인터를 재캡처합니다.
            // 버퍼가 재할당된 경우 data[] 포인터가 변경될 수 있습니다.
            var dstData    = new byte_ptrArray8();
            var dstStrides = new int_array8();
            for (uint i = 0; i < 8; i++)
            {
                dstData[i]    = dstFrame->data[i];
                dstStrides[i] = dstFrame->linesize[i];
            }

            ffmpeg.sws_scale(swsCtx,
                srcData, srcStrides, 0, srcFrame->height,
                dstData, dstStrides);

            ffmpeg.av_frame_unref(srcFrame);

            // ── GIF 프레임 pts 설정 ───────────────────────────────────
            dstFrame->pts = gifPts;
            gifPts += gifFrameDur;

            // ── 인코딩 ───────────────────────────────────────────────
            ret = ffmpeg.avcodec_send_frame(encCtx, dstFrame);
            if (ret < 0) continue;

            while (true)
            {
                ret = ffmpeg.avcodec_receive_packet(encCtx, encPkt);
                if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF) break;
                if (ret < 0) break;

                encPkt->stream_index = outStream->index;
                ffmpeg.av_packet_rescale_ts(encPkt, encCtx->time_base, outStream->time_base);
                ffmpeg.av_interleaved_write_frame(outCtx, encPkt);
                ffmpeg.av_packet_unref(encPkt);
            }

            // 진행률 보고
            if (totalSec > 0)
            {
                double elapsed = (frameTime - startTime).TotalSeconds;
                progress?.Report(Math.Clamp(elapsed / totalSec, 0.0, 0.99));
            }
        }
    }

    private static unsafe void FlushGifEncoder(
        AVCodecContext* encCtx, AVFormatContext* outCtx, AVPacket* encPkt, AVStream* outStream)
    {
        ffmpeg.avcodec_send_frame(encCtx, null); // flush
        while (true)
        {
            int ret = ffmpeg.avcodec_receive_packet(encCtx, encPkt);
            if (ret == AVERROR_EAGAIN || ret == ffmpeg.AVERROR_EOF) break;
            if (ret < 0) break;

            encPkt->stream_index = outStream->index;
            ffmpeg.av_packet_rescale_ts(encPkt, encCtx->time_base, outStream->time_base);
            ffmpeg.av_interleaved_write_frame(outCtx, encPkt);
            ffmpeg.av_packet_unref(encPkt);
        }
    }

    // ── 유틸리티 ─────────────────────────────────────────────────────

    private static unsafe string GetErrorMessage(int errorCode)
    {
        const int bufSize = 256;
        var buf = stackalloc byte[bufSize];
        ffmpeg.av_make_error_string(buf, (ulong)bufSize, errorCode);
        return Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0')
               ?? $"FFmpeg error {errorCode}";
    }
}
