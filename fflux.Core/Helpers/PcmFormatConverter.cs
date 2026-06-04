using fflux.Core.Models;

namespace fflux.Core.Helpers;

/// <summary>
/// FFmpeg <c>swresample</c>을 사용하여 <c>AVFrame</c>의 오디오 데이터를
/// IEEE float (32-bit) 인터리브 PCM으로 변환합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>출력 포맷은 항상 <c>AV_SAMPLE_FMT_FLT</c> (인터리브 float)입니다.</item>
///   <item>출력 채널은 최대 2ch (스테레오)로 제한합니다.
///         3ch 이상 입력은 SwrContext가 자동으로 스테레오 다운믹스합니다.</item>
///   <item>입력 파라미터가 변경되면 <c>SwrContext</c>를 자동으로 재생성합니다.</item>
///   <item>이 클래스는 스레드 안전하지 않습니다. 호출자 측에서 직렬화하세요.</item>
/// </list>
/// </remarks>
internal sealed unsafe class PcmFormatConverter : IDisposable
{
    // ── SwrContext 캐시 ──────────────────────────────────────────────

    private SwrContext*    _swrCtx;
    private AVSampleFormat _cachedSrcFmt;
    private int            _cachedSrcRate;
    private int            _cachedSrcChannels;
    private int            _cachedDstChannels;  // 실제 출력 채널 수 (≤ 2)

    private bool _disposed;

    // ── 공개 API ────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="srcFrame"/>을 인터리브 IEEE float (최대 스테레오)로 변환한
    /// <see cref="AudioFrame"/>을 반환합니다.
    /// </summary>
    public AudioFrame Convert(AVFrame* srcFrame, TimeSpan timestamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var srcFmt      = (AVSampleFormat)srcFrame->format;
        int srcRate     = srcFrame->sample_rate;
        int srcChannels = srcFrame->ch_layout.nb_channels;
        int nbSamples   = srcFrame->nb_samples;

        EnsureContext(srcFrame, srcFmt, srcRate, srcChannels);

        int dstChannels = _cachedDstChannels;
        var result      = new float[nbSamples * dstChannels];

        fixed (float* dstFloatPtr = result)
        {
            byte* dstBytePtr = (byte*)dstFloatPtr;
            byte** dstData   = stackalloc byte*[1] { dstBytePtr };

            byte** srcData = stackalloc byte*[8];
            for (uint i = 0; i < 8; i++)
                srcData[i] = srcFrame->data[i];

            int converted = ffmpeg.swr_convert(
                _swrCtx,
                dstData, nbSamples,
                srcData, nbSamples);

            if (converted < 0)
                throw new InvalidOperationException(
                    $"swr_convert 실패 (에러코드: {converted})");

            return new AudioFrame(
                timestamp:   timestamp,
                samples:     converted < nbSamples ? result[..(converted * dstChannels)] : result,
                sampleRate:  srcRate,
                channels:    dstChannels,
                sampleCount: converted);
        }
    }

    // ── 내부 ────────────────────────────────────────────────────────

    private void EnsureContext(
        AVFrame*       frame,
        AVSampleFormat srcFmt,
        int            srcRate,
        int            srcChannels)
    {
        if (_swrCtx != null
            && srcFmt      == _cachedSrcFmt
            && srcRate     == _cachedSrcRate
            && srcChannels == _cachedSrcChannels)
            return;

        if (_swrCtx != null)
        {
            var ctx = _swrCtx;
            ffmpeg.swr_free(&ctx);
            _swrCtx = null;
        }

        // waveOutOpen은 대부분의 드라이버에서 2ch까지만 지원합니다.
        // 3ch 이상은 FFmpeg swresample이 스테레오(FL+FR)로 다운믹스합니다.
        int dstChannels = srcChannels > 2 ? 2 : srcChannels;

        _swrCtx = ffmpeg.swr_alloc();
        if (_swrCtx == null)
            throw new InvalidOperationException("SwrContext 할당 실패.");

        // 입력 파라미터
        ffmpeg.av_opt_set_chlayout   (_swrCtx, "in_chlayout",    &frame->ch_layout, 0);
        ffmpeg.av_opt_set_int        (_swrCtx, "in_sample_rate",  srcRate,           0);
        ffmpeg.av_opt_set_sample_fmt (_swrCtx, "in_sample_fmt",   srcFmt,            0);

        // 출력 파라미터: 채널은 최대 스테레오, 포맷은 float 인터리브
        AVChannelLayout dstLayout = default;
        ffmpeg.av_channel_layout_default(&dstLayout, dstChannels);
        ffmpeg.av_opt_set_chlayout   (_swrCtx, "out_chlayout",    &dstLayout,                        0);
        ffmpeg.av_opt_set_int        (_swrCtx, "out_sample_rate",  srcRate,                           0);
        ffmpeg.av_opt_set_sample_fmt (_swrCtx, "out_sample_fmt",   AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);

        int ret = ffmpeg.swr_init(_swrCtx);
        if (ret < 0)
            throw new InvalidOperationException(
                $"SwrContext 초기화 실패: {srcFmt} {srcRate}Hz {srcChannels}ch → float {dstChannels}ch");

        _cachedSrcFmt      = srcFmt;
        _cachedSrcRate     = srcRate;
        _cachedSrcChannels = srcChannels;
        _cachedDstChannels = dstChannels;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_swrCtx != null)
        {
            var ctx = _swrCtx;
            ffmpeg.swr_free(&ctx);
            _swrCtx = null;
        }
    }
}
