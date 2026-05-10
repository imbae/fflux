using fflux.Core.Models;

namespace fflux.Core.Helpers;

/// <summary>
/// FFmpeg <c>swscale</c>를 사용하여 <c>AVFrame</c>을 BGRA 32-bit 포맷으로 변환합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>출력 포맷은 항상 <c>AV_PIX_FMT_BGRA</c>입니다 (WPF <c>WriteableBitmap</c> 호환).</item>
///   <item>입력 크기/포맷이 변경되면 <c>SwsContext</c>를 자동으로 재생성합니다.</item>
///   <item>이 클래스는 스레드 안전하지 않습니다. 호출자 측에서 직렬화하세요.</item>
/// </list>
/// </remarks>
internal sealed unsafe class PixelFormatConverter : IDisposable
{
    // ffmpeg.autogen 8.1.0에서 SWS_BILINEAR는 노출되지 않음 — 원시값 사용
    private const int SWS_BILINEAR = 2;

    // ── SwsContext 캐시 ──────────────────────────────────────────────

    private SwsContext*   _swsCtx;
    private int           _cachedSrcW;
    private int           _cachedSrcH;
    private AVPixelFormat _cachedSrcFmt;
    private int           _cachedDstW;
    private int           _cachedDstH;

    private bool _disposed;

    // ── 공개 API ────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="srcFrame"/>을 BGRA로 변환한 <see cref="VideoFrame"/>을 반환합니다.
    /// </summary>
    /// <param name="srcFrame">디코딩된 원본 AVFrame</param>
    /// <param name="dstWidth">출력 너비  (srcFrame과 다르면 스케일링)</param>
    /// <param name="dstHeight">출력 높이 (srcFrame과 다르면 스케일링)</param>
    /// <param name="timestamp">프레임 타임스탬프 (호출자가 계산)</param>
    public VideoFrame Convert(AVFrame* srcFrame, int dstWidth, int dstHeight, TimeSpan timestamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var srcFmt = (AVPixelFormat)srcFrame->format;
        EnsureContext(srcFrame->width, srcFrame->height, srcFmt, dstWidth, dstHeight);

        // BGRA = 4바이트/픽셀
        int stride = dstWidth * 4;
        var data   = new byte[dstHeight * stride];

        // ── 소스 배열 구성 ──────────────────────────────────────────
        // byte_ptrArray8 / int_array8 의 인덱서는 uint를 요구합니다.
        var srcDataArr     = new byte*[8];
        var srcLinesizeArr = new int[8];
        for (uint i = 0; i < 8; i++)
        {
            srcDataArr[i]     = srcFrame->data[i];
            srcLinesizeArr[i] = srcFrame->linesize[i];
        }

        // ── 목적지 배열 구성 (BGRA = 평면 1개) ──────────────────────
        fixed (byte* dstPtr = data)
        {
            // sws_scale(ffmpeg.autogen 8.1.0)는 byte*[] / int[] 관리 배열을 받습니다.
            var dstDataArr     = new byte*[] { dstPtr, null, null, null };
            var dstLinesizeArr = new int[]   { stride,  0,    0,    0   };

            ffmpeg.sws_scale(
                _swsCtx,
                srcDataArr,    srcLinesizeArr,
                0,             srcFrame->height,
                dstDataArr,    dstLinesizeArr);
        }

        return new VideoFrame(dstWidth, dstHeight, stride, data, timestamp);
    }

    // ── 내부 ────────────────────────────────────────────────────────

    /// <summary>캐시된 SwsContext가 현재 입력에 맞지 않으면 재생성합니다.</summary>
    private void EnsureContext(
        int srcW, int srcH, AVPixelFormat srcFmt,
        int dstW, int dstH)
    {
        if (_swsCtx != null
            && srcW   == _cachedSrcW
            && srcH   == _cachedSrcH
            && srcFmt == _cachedSrcFmt
            && dstW   == _cachedDstW
            && dstH   == _cachedDstH)
            return;

        if (_swsCtx != null)
            ffmpeg.sws_freeContext(_swsCtx);

        _swsCtx = ffmpeg.sws_getContext(
            srcW, srcH, srcFmt,
            dstW, dstH, AVPixelFormat.AV_PIX_FMT_BGRA,
            SWS_BILINEAR,
            null, null, null);

        if (_swsCtx == null)
            throw new InvalidOperationException(
                $"SwsContext 생성 실패: {srcFmt} {srcW}×{srcH} → BGRA {dstW}×{dstH}");

        _cachedSrcW   = srcW;
        _cachedSrcH   = srcH;
        _cachedSrcFmt = srcFmt;
        _cachedDstW   = dstW;
        _cachedDstH   = dstH;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_swsCtx != null)
        {
            ffmpeg.sws_freeContext(_swsCtx);
            _swsCtx = null;
        }
    }
}
