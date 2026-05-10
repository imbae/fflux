using System.Collections.ObjectModel;
using System.Text;
using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.Core.Models.StreamInfo;

namespace fflux.Core.Demuxers;

/// <summary>
/// ffmpeg.autogen의 <c>AVFormatContext</c>를 사용하여 미디어 파일의
/// 컨테이너 및 스트림 정보를 추출합니다.
/// </summary>
/// <remarks>
/// 모든 unsafe 포인터 연산은 이 클래스 내부에 격리됩니다.
/// 외부에서는 <see cref="IMediaFileReader"/> 인터페이스만 사용하세요.
/// </remarks>
public sealed unsafe class MediaFileReader : IMediaFileReader
{
    private readonly IFFmpegInitializer _initializer;
    private readonly ILogger<MediaFileReader> _logger;

    public MediaFileReader(
        IFFmpegInitializer initializer,
        ILogger<MediaFileReader> logger)
    {
        _initializer = initializer;
        _logger      = logger;
    }

    // ── IMediaFileReader 구현 ────────────────────────────────────────

    /// <inheritdoc/>
    public Task<MediaInfo> ReadAsync(string filePath, CancellationToken ct = default)
    {
        if (!_initializer.IsInitialized)
            throw new InvalidOperationException(
                "FFmpeg가 초기화되지 않았습니다. " +
                "설정에서 FFmpeg LGPL 바이너리 경로를 지정한 후 저장하세요.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.", filePath);

        // AVFormatContext 조작은 I/O + CPU 작업이므로 ThreadPool에서 실행
        return Task.Run(() => ReadCore(filePath), ct);
    }

    // ── 내부 구현 (unsafe 격리 영역) ────────────────────────────────

    private MediaInfo ReadCore(string filePath)
    {
        _logger.LogDebug("미디어 파일 읽기 시작: {Path}", filePath);

        AVFormatContext* fmtCtx = null;

        try
        {
            // ── 1. avformat_open_input ───────────────────────────────
            int ret = ffmpeg.avformat_open_input(&fmtCtx, filePath, null, null);
            if (ret < 0)
                throw new MediaReadException(
                    $"파일을 열 수 없습니다: {filePath}\n{GetErrorMessage(ret)}", ret);

            // ── 2. avformat_find_stream_info ─────────────────────────
            ret = ffmpeg.avformat_find_stream_info(fmtCtx, null);
            if (ret < 0)
                throw new MediaReadException(
                    $"스트림 정보를 추출할 수 없습니다: {GetErrorMessage(ret)}", ret);

            // ── 3. 스트림 파싱 ───────────────────────────────────────
            var videoStreams    = new List<VideoStreamInfo>();
            var audioStreams    = new List<AudioStreamInfo>();
            var subtitleStreams = new List<SubtitleStreamInfo>();

            for (uint i = 0; i < fmtCtx->nb_streams; i++)
            {
                var stream   = fmtCtx->streams[i];
                var codecPar = stream->codecpar;

                switch (codecPar->codec_type)
                {
                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        videoStreams.Add(ParseVideoStream(stream, (int)i));
                        break;
                    case AVMediaType.AVMEDIA_TYPE_AUDIO:
                        audioStreams.Add(ParseAudioStream(stream, (int)i));
                        break;
                    case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        subtitleStreams.Add(ParseSubtitleStream(stream, (int)i));
                        break;
                }
            }

            // ── 4. 컨테이너 메타데이터 수집 ─────────────────────────
            var fileInfo = new FileInfo(filePath);

            var mediaInfo = new MediaInfo
            {
                FilePath       = filePath,
                FileName       = fileInfo.Name,
                FileSize       = fileInfo.Length,
                FormatName     = PtrToString(fmtCtx->iformat->name)     ?? string.Empty,
                FormatLongName = PtrToString(fmtCtx->iformat->long_name) ?? string.Empty,
                Duration       = fmtCtx->duration > 0
                    ? TimeSpan.FromSeconds(fmtCtx->duration / (double)ffmpeg.AV_TIME_BASE)
                    : TimeSpan.Zero,
                BitRate        = fmtCtx->bit_rate,
                VideoStreams    = new ReadOnlyCollection<VideoStreamInfo>(videoStreams),
                AudioStreams    = new ReadOnlyCollection<AudioStreamInfo>(audioStreams),
                SubtitleStreams = new ReadOnlyCollection<SubtitleStreamInfo>(subtitleStreams),
            };

            _logger.LogInformation(
                "미디어 파일 파싱 완료: {File} — {Format} | {Duration} | " +
                "비디오 {V}개 | 오디오 {A}개 | 자막 {S}개",
                fileInfo.Name, mediaInfo.FormatName, mediaInfo.DurationText,
                videoStreams.Count, audioStreams.Count, subtitleStreams.Count);

            return mediaInfo;
        }
        finally
        {
            // AVFormatContext는 항상 명시적으로 닫아야 합니다.
            if (fmtCtx != null)
                ffmpeg.avformat_close_input(&fmtCtx);
        }
    }

    // ── 스트림 파서 ──────────────────────────────────────────────────

    private static VideoStreamInfo ParseVideoStream(AVStream* stream, int index)
    {
        var par   = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(par->codec_id);

        // 평균 프레임레이트 계산
        double fps = stream->avg_frame_rate.den != 0
            ? stream->avg_frame_rate.num / (double)stream->avg_frame_rate.den
            : 0;

        // 픽셀 포맷 이름 (av_get_pix_fmt_name은 managed string 반환)
        var pixFmt = par->format != (int)AVPixelFormat.AV_PIX_FMT_NONE
            ? ffmpeg.av_get_pix_fmt_name((AVPixelFormat)par->format) ?? "unknown"
            : "unknown";

        // 코덱 프로파일 이름
        var profile = par->profile >= 0
            ? ffmpeg.avcodec_profile_name(par->codec_id, par->profile)
            : null;

        return new VideoStreamInfo
        {
            StreamIndex  = index,
            CodecName    = codec != null ? PtrToString(codec->name)      ?? string.Empty : string.Empty,
            CodecLongName= codec != null ? PtrToString(codec->long_name) ?? string.Empty : string.Empty,
            Profile      = profile,
            Width        = par->width,
            Height       = par->height,
            FrameRate    = fps,
            PixelFormat  = pixFmt,
            BitRate      = par->bit_rate,
            Duration     = CalcDuration(stream),
        };
    }

    private static AudioStreamInfo ParseAudioStream(AVStream* stream, int index)
    {
        var par   = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(par->codec_id);

        // 채널 레이아웃 설명 (AVChannelLayout 사용)
        var channelLayout = DescribeChannelLayout(&par->ch_layout);

        // 샘플 포맷 이름 (managed string 반환)
        var sampleFmt = par->format >= 0
            ? ffmpeg.av_get_sample_fmt_name((AVSampleFormat)par->format) ?? "unknown"
            : "unknown";

        // 언어 메타데이터
        var lang = GetMetadataValue(stream->metadata, "language");

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
            Language      = lang,
        };
    }

    private static SubtitleStreamInfo ParseSubtitleStream(AVStream* stream, int index)
    {
        var par   = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(par->codec_id);

        return new SubtitleStreamInfo
        {
            StreamIndex = index,
            CodecName   = codec != null ? PtrToString(codec->name) ?? string.Empty : string.Empty,
            Language    = GetMetadataValue(stream->metadata, "language"),
            Title       = GetMetadataValue(stream->metadata, "title"),
        };
    }

    // ── 포인터 유틸리티 ──────────────────────────────────────────────

    /// <summary>AVChannelLayout에서 채널 레이아웃 설명 문자열을 가져옵니다.</summary>
    private static string DescribeChannelLayout(AVChannelLayout* chLayout)
    {
        const int bufSize = 64;
        var buf = stackalloc byte[bufSize];
        int result = ffmpeg.av_channel_layout_describe(chLayout, buf, (ulong)bufSize);
        if (result < 0) return "unknown";
        return Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0') ?? "unknown";
    }

    /// <summary>AVDictionary에서 특정 키의 값을 반환합니다. 없으면 null.</summary>
    private static string? GetMetadataValue(AVDictionary* dict, string key)
    {
        if (dict == null) return null;
        var entry = ffmpeg.av_dict_get(dict, key, null, 0);
        return entry != null ? PtrToString(entry->value) : null;
    }

    /// <summary>스트림의 time_base 기반으로 재생 길이를 계산합니다.</summary>
    private static TimeSpan CalcDuration(AVStream* stream)
    {
        if (stream->duration <= 0 || stream->time_base.den == 0)
            return TimeSpan.Zero;

        double seconds = stream->duration
            * stream->time_base.num
            / (double)stream->time_base.den;

        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    /// <summary>byte* (UTF-8 C 문자열)을 managed string으로 변환합니다.</summary>
    private static string? PtrToString(byte* ptr)
        => ptr == null ? null : Marshal.PtrToStringUTF8((IntPtr)ptr);

    // ── FFmpeg 에러 포매팅 ────────────────────────────────────────────

    /// <summary>FFmpeg 오류 코드를 사람이 읽을 수 있는 문자열로 변환합니다.</summary>
    private static string GetErrorMessage(int errorCode)
    {
        const int bufSize = 256;
        var buf = stackalloc byte[bufSize];
        ffmpeg.av_make_error_string(buf, (ulong)bufSize, errorCode);
        return Marshal.PtrToStringUTF8((IntPtr)buf)?.TrimEnd('\0')
               ?? $"FFmpeg error {errorCode}";
    }
}
