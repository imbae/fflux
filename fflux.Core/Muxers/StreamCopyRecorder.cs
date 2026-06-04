using fflux.Core.Abstractions;
using fflux.Core.Exceptions;

namespace fflux.Core.Muxers;

/// <summary>
/// ffmpeg.autogen Stream Copy 녹화기.
/// <para>
/// 재인코딩 없이 AVPacket을 원본 그대로 출력 컨테이너에 기록합니다.
/// LGPL 안전: 코덱 라이브러리를 링킹하지 않습니다.
/// </para>
/// <para>
/// 구현 패턴: <c>unsafe class</c>는 async 메서드에서 CS4004를 유발하므로
/// nint 핸들로 포인터를 저장하고, 포인터 접근이 필요한 부분만 <c>unsafe</c> 동기 메서드로 분리합니다.
/// </para>
/// </summary>
public sealed class StreamCopyRecorder : IStreamCopyRecorder
{
    private readonly IFFmpegInitializer _initializer;
    private readonly ILogger<StreamCopyRecorder> _logger;

    // AVERROR(EAGAIN) = -11 (플랫폼 공통)
    private const int AVERROR_EAGAIN = -11;

    // ── FFmpeg 핸들 (nint = IntPtr) ──────────────────────────────────
    private nint _inputHandle;   // AVFormatContext*  (입력)
    private nint _outputHandle;  // AVFormatContext*  (출력)
    private nint _packetHandle;  // AVPacket*         (재사용)

    // 입력 스트림 인덱스 → AVBSFContext* 핸들 (0: BSF 미사용)
    // H.264/HEVC AVCC 포맷의 MP4를 MKV 등으로 복사할 때 Annex B 변환에 필요합니다.
    private nint[] _bsfHandles = [];

    // 입력 스트림 인덱스 → 출력 스트림 인덱스 매핑 (-1: 해당 스트림 무시)
    private int[] _streamMapping = [];

    // 각 스트림의 첫 번째 DTS (타임스탬프 기준점 정규화용)
    private long[] _firstDts = [];

    // ── 헤더 기록 완료 여부 (WriteTrailerSafe에서 사전 검사) ─────────
    // avformat_write_header 전에 av_write_trailer를 호출하면 native crash 발생
    private bool _headerWritten;

    // ── 실시간 속도 제한 (전체 파일 복사 방지) ───────────────────────
    //
    // 입력 파일을 디스크 속도로 읽으면 1시간짜리 파일을 수 초 만에 모두 복사합니다.
    // 첫 패킷의 입력 DTS를 기준으로 경과 시간을 계산하여 실시간 속도로 제한합니다.
    private double _firstPktInputSec = -1.0;
    private double _lastPktInputSec  = -1.0;

    // ── 상태 ────────────────────────────────────────────────────────
    private volatile bool _stopRequested;
    private bool _disposed;

    // 녹화 루프 태스크 (StopAsync에서 완료 대기)
    private Task? _recordTask;
    private CancellationTokenSource? _cts;

    // ── 경과 시간 추적 ───────────────────────────────────────────────
    private readonly System.Diagnostics.Stopwatch _sw = new();

    // ── IStreamCopyRecorder 구현 ─────────────────────────────────────

    /// <inheritdoc/>
    public bool IsRecording { get; private set; }

    /// <inheritdoc/>
    public string? LastError { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<TimeSpan>? DurationUpdated;

    public StreamCopyRecorder(IFFmpegInitializer initializer, ILogger<StreamCopyRecorder> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(string sourcePath, string outputPath,
                           TimeSpan startTime = default,
                           CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initializer.IsInitialized)
            throw new InvalidOperationException("FFmpeg가 초기화되지 않았습니다.");

        if (IsRecording)
            throw new InvalidOperationException("이미 녹화 중입니다.");

        _stopRequested   = false;
        _firstPktInputSec = -1.0;
        _lastPktInputSec  = -1.0;
        _headerWritten   = false;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _recordTask = Task.Run(
            () => RecordCore(sourcePath, outputPath, startTime, token), token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _stopRequested = true;
        _cts?.Cancel();

        if (_recordTask is not null)
        {
            try { await _recordTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "녹화 루프 종료 중 예외"); }
        }

        IsRecording = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // 내부 녹화 루프 (ThreadPool — unsafe 헬퍼 호출만)
    // ═══════════════════════════════════════════════════════════════

    private async Task RecordCore(string sourcePath, string outputPath,
                                  TimeSpan startTime, CancellationToken ct)
    {
        IsRecording = true;
        LastError = null;
        _sw.Restart();

        try
        {
            // 1. 컨텍스트 열기 (unsafe 헬퍼)
            OpenContexts(sourcePath, outputPath, startTime);

            _logger.LogInformation(
                "Stream Copy 녹화 시작: {Src} → {Dst} (시작: {Start:g})",
                Path.GetFileName(sourcePath), Path.GetFileName(outputPath), startTime);

            // 2. 패킷 복사 루프
            // System.Timers.Timer: ThreadPool에서 독립적으로 발화하므로
            // Task.Delay 대기 중에도 DurationUpdated 이벤트가 1초마다 정확히 발생합니다.
            // (Stopwatch 폴링은 루프가 Task.Delay에 블로킹되면 체크가 지연됩니다.)
            using var durationTimer = new System.Timers.Timer(1000) { AutoReset = true };
            durationTimer.Elapsed += (_, _) =>
            {
                if (IsRecording)
                    DurationUpdated?.Invoke(this, _sw.Elapsed);
            };
            durationTimer.Start();

            while (!ct.IsCancellationRequested && !_stopRequested)
            {
                bool eof = ReadAndWritePacket();
                if (eof)
                {
                    _logger.LogInformation("녹화: 파일 끝에 도달하여 종료");
                    break;
                }

                // ── 실시간 속도 제한 ──────────────────────────────────────
                // 첫 패킷의 입력 DTS를 기준으로 경과 시간을 계산하여
                // 실제 벽시계 시간보다 300ms 이상 앞서가면 대기합니다.
                // 이렇게 하면 "녹화 시작~종료" 사이의 구간만 출력 파일에 기록됩니다.
                if (_lastPktInputSec >= 0)
                {
                    if (_firstPktInputSec < 0)
                        _firstPktInputSec = _lastPktInputSec;

                    var expectedMs = (_lastPktInputSec - _firstPktInputSec) * 1000.0;
                    var actualMs   = _sw.Elapsed.TotalMilliseconds;

                    if (expectedMs > actualMs + 300.0)
                    {
                        var sleepMs = (int)Math.Min(expectedMs - actualMs - 300.0, 2000.0);
                        if (sleepMs > 0)
                            await Task.Delay(sleepMs, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    // DTS 정보가 없는 경우 최소 양보
                    await Task.Yield();
                }
            }

            // 3. 트레일러 기록
            WriteTrailer();

            _logger.LogInformation(
                "Stream Copy 녹화 완료: {Duration:g}", _sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            WriteTrailerSafe();
            _logger.LogInformation("Stream Copy 녹화 취소됨");
        }
        catch (Exception ex)
        {
            WriteTrailerSafe();
            LastError = ex.Message;
            _logger.LogError(ex, "Stream Copy 녹화 오류");
            // 재throw 하지 않음: ViewModel의 DurationUpdated 핸들러에서 LastError를 읽어 처리
        }
        finally
        {
            ReleaseContexts();
            IsRecording = false;
            DurationUpdated?.Invoke(this, _sw.Elapsed);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // unsafe 헬퍼 (포인터 접근 전용)
    // ═══════════════════════════════════════════════════════════════

    private unsafe void OpenContexts(string sourcePath, string outputPath, TimeSpan startTime)
    {
        // ── 입력 열기 ─────────────────────────────────────────────────
        AVFormatContext* inCtx = null;
        int ret = ffmpeg.avformat_open_input(&inCtx, sourcePath, null, null);
        if (ret < 0)
            throw new MediaReadException(
                $"녹화 입력 파일 열기 실패: {GetErrorMessage(ret)}", ret);

        _inputHandle = (nint)inCtx;

        ret = ffmpeg.avformat_find_stream_info(inCtx, null);
        if (ret < 0)
            throw new MediaReadException(
                $"스트림 정보 추출 실패: {GetErrorMessage(ret)}", ret);

        // ── 시작 위치 seek ────────────────────────────────────────────
        if (startTime > TimeSpan.Zero)
        {
            long ts = (long)(startTime.TotalSeconds * ffmpeg.AV_TIME_BASE);
            int seekRet = ffmpeg.av_seek_frame(inCtx, -1, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);
            if (seekRet < 0)
                _logger.LogWarning("녹화 seek 실패 ({Start:g}): {Err}", startTime, GetErrorMessage(seekRet));
        }

        // ── 출력 컨텍스트 할당 (확장자로 포맷 자동 결정) ─────────────
        AVFormatContext* outCtx = null;
        ret = ffmpeg.avformat_alloc_output_context2(&outCtx, null, null, outputPath);
        if (ret < 0 || outCtx == null)
            throw new MediaReadException(
                $"출력 컨텍스트 할당 실패: {GetErrorMessage(ret)}", ret);

        _outputHandle = (nint)outCtx;

        // 출력 포맷 감지 실패 시 native crash 방지
        if (outCtx->oformat == null)
            throw new MediaReadException(
                $"출력 파일 포맷을 인식할 수 없습니다: '{Path.GetExtension(outputPath)}'. " +
                $".mkv, .mp4, .ts 등의 확장자를 사용하세요.");

        // ── 스트림 매핑: 입력 → 출력 ─────────────────────────────────
        int inStreamCount = (int)inCtx->nb_streams;
        _streamMapping = new int[inStreamCount];
        _firstDts      = new long[inStreamCount];
        int outIdx     = 0;

        for (int i = 0; i < inStreamCount; i++)
        {
            _firstDts[i] = ffmpeg.AV_NOPTS_VALUE;
            var inStream = inCtx->streams[i];
            var mtype    = inStream->codecpar->codec_type;

            // ── Attached Picture 스트림 제외 ──────────────────────────
            // MP4/MOV 파일의 썸네일 스트림(AVMEDIA_TYPE_VIDEO)은
            // AV_DISPOSITION_ATTACHED_PIC 플래그로 구분합니다.
            // MKV 등 출력 컨테이너에 이 스트림을 그대로 넣으면
            // 단일 패킷의 특수 처리 과정에서 native crash가 발생할 수 있습니다.
            if ((inStream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0)
            {
                _logger.LogDebug("스트림 #{Idx} 제외: Attached Picture (썸네일)", i);
                _streamMapping[i] = -1;
                continue;
            }

            // 비디오 · 오디오 · 자막만 복사
            if (mtype != AVMediaType.AVMEDIA_TYPE_VIDEO   &&
                mtype != AVMediaType.AVMEDIA_TYPE_AUDIO   &&
                mtype != AVMediaType.AVMEDIA_TYPE_SUBTITLE)
            {
                _streamMapping[i] = -1;
                continue;
            }

            // ── 출력 포맷이 해당 코덱을 지원하는지 확인 ─────────────────
            // mov_text(MP4 자막) → MKV 처럼 컨테이너 간 코덱 호환성 문제를
            // 사전에 차단합니다. avformat_query_codec이 0을 반환하면 미지원.
            // -1(불명확)은 일단 포함하여 muxer에게 맡깁니다.
            int codecSupport = ffmpeg.avformat_query_codec(
                outCtx->oformat, inStream->codecpar->codec_id, ffmpeg.FF_COMPLIANCE_NORMAL);
            if (codecSupport == 0)
            {
                _logger.LogWarning(
                    "스트림 #{Idx} ({Type} / {Codec}) 제외: 출력 포맷 '{Fmt}'에서 지원하지 않음",
                    i, mtype, inStream->codecpar->codec_id,
                    Marshal.PtrToStringUTF8((IntPtr)outCtx->oformat->name));
                _streamMapping[i] = -1;
                continue;
            }

            _streamMapping[i] = outIdx++;

            // 출력 스트림 추가
            var outStream = ffmpeg.avformat_new_stream(outCtx, null);
            if (outStream == null)
                throw new MediaReadException("출력 스트림 추가 실패.");

            ret = ffmpeg.avcodec_parameters_copy(outStream->codecpar, inStream->codecpar);
            if (ret < 0)
                throw new MediaReadException(
                    $"코덱 파라미터 복사 실패: {GetErrorMessage(ret)}", ret);

            // 컨테이너 변환 시 codec_tag 호환성 초기화
            outStream->codecpar->codec_tag = 0;
        }

        if (outIdx == 0)
            throw new MediaReadException("녹화할 수 있는 스트림이 없습니다 (비디오/오디오/자막).");

        // ── 출력 파일 열기 ────────────────────────────────────────────
        if ((outCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            AVIOContext* pb = null;
            ret = ffmpeg.avio_open(&pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
                throw new MediaReadException(
                    $"출력 파일 열기 실패: {GetErrorMessage(ret)}", ret);
            outCtx->pb = pb;
        }

        // ── BitStream Filter 설정 ────────────────────────────────────
        // H.264/HEVC AVCC 포맷을 MKV/TS 출력에 맞는 Annex B로 변환합니다.
        // avformat_write_header 전에 완료되어야 출력 codecpar가 올바르게 설정됩니다.
        SetupBitStreamFilters(inCtx, outCtx);

        // ── 헤더 기록 ────────────────────────────────────────────────
        ret = ffmpeg.avformat_write_header(outCtx, null);
        if (ret < 0)
            throw new MediaReadException(
                $"헤더 기록 실패: {GetErrorMessage(ret)}", ret);

        // avformat_write_header 성공 후에만 av_write_trailer 호출 허용
        _headerWritten = true;

        // ── 패킷 할당 ────────────────────────────────────────────────
        var pkt = ffmpeg.av_packet_alloc();
        if (pkt == null)
            throw new MediaReadException("AVPacket 할당 실패.");
        _packetHandle = (nint)pkt;
    }

    /// <summary>
    /// 입력에서 패킷을 하나 읽어 타임스탬프를 정규화한 후 출력에 기록합니다.
    /// EOF이면 true를 반환합니다. 부수 효과로 <see cref="_lastPktInputSec"/>를 갱신합니다.
    /// </summary>
    private unsafe bool ReadAndWritePacket()
    {
        if (_inputHandle == 0 || _outputHandle == 0 || _packetHandle == 0)
            return true;

        var inCtx  = (AVFormatContext*)_inputHandle;
        var outCtx = (AVFormatContext*)_outputHandle;
        var pkt    = (AVPacket*)_packetHandle;

        int ret = ffmpeg.av_read_frame(inCtx, pkt);

        if (ret == ffmpeg.AVERROR_EOF) return true;

        if (ret < 0)
        {
            _logger.LogWarning("패킷 읽기 오류: {Err}", GetErrorMessage(ret));
            return true;
        }

        int inIdx = pkt->stream_index;

        // 범위 검사 및 매핑되지 않은 스트림 건너뜀
        if (inIdx < 0 || inIdx >= _streamMapping.Length || _streamMapping[inIdx] < 0)
        {
            ffmpeg.av_packet_unref(pkt);
            return false;
        }

        int outIdx    = _streamMapping[inIdx];
        int outCount  = (int)outCtx->nb_streams;

        // 출력 스트림 인덱스 범위 검사 (방어적 코딩)
        if (outIdx < 0 || outIdx >= outCount)
        {
            ffmpeg.av_packet_unref(pkt);
            return false;
        }

        var inStream  = inCtx->streams[inIdx];
        var outStream = outCtx->streams[outIdx];

        // ── DTS=PTS 폴백 ─────────────────────────────────────────────
        // B프레임 없는 MP4 등 일부 컨테이너는 pkt->dts == AV_NOPTS_VALUE 입니다.
        // DTS 없이 진행하면 av_interleaved_write_frame이 실패하므로 PTS를 사용합니다.
        if (pkt->dts == ffmpeg.AV_NOPTS_VALUE && pkt->pts != ffmpeg.AV_NOPTS_VALUE)
            pkt->dts = pkt->pts;

        // ── BitStream Filter 적용 ─────────────────────────────────────
        // H.264/HEVC AVCC → Annex B 변환 (MP4→MKV 복사 시 필수)
        if (inIdx < _bsfHandles.Length && _bsfHandles[inIdx] != 0)
        {
            var bsfCtx = (AVBSFContext*)_bsfHandles[inIdx];

            // BSF에 패킷 전달 (내부적으로 패킷 버퍼를 소비합니다)
            int bsfRet = ffmpeg.av_bsf_send_packet(bsfCtx, pkt);
            if (bsfRet < 0)
            {
                ffmpeg.av_packet_unref(pkt);
                _logger.LogWarning("BSF send_packet 실패: {Err}", GetErrorMessage(bsfRet));
                return false;
            }

            // BSF 출력 패킷 수신
            bsfRet = ffmpeg.av_bsf_receive_packet(bsfCtx, pkt);
            if (bsfRet == AVERROR_EAGAIN || bsfRet == ffmpeg.AVERROR_EOF)
            {
                // 이번 입력에서 출력 없음 — 다음 패킷으로 계속
                return false;
            }
            if (bsfRet < 0)
            {
                _logger.LogWarning("BSF receive_packet 실패: {Err}", GetErrorMessage(bsfRet));
                return false;
            }
            // pkt는 이제 Annex B 변환된 데이터를 담고 있습니다.
        }

        // ── 실시간 속도 제한을 위한 입력 DTS 추적 (정규화 전) ─────────
        // 정규화 전 raw DTS를 초 단위로 변환하여 속도 제한 계산에 사용합니다.
        if (pkt->dts != ffmpeg.AV_NOPTS_VALUE && inStream->time_base.den > 0)
        {
            double rawSec = pkt->dts * (double)inStream->time_base.num
                                     / inStream->time_base.den;
            if (rawSec > _lastPktInputSec)
                _lastPktInputSec = rawSec;
        }

        // ── 첫 DTS 기록 (타임스탬프 기준점) ──────────────────────────
        long baseDts = pkt->dts != ffmpeg.AV_NOPTS_VALUE ? pkt->dts : pkt->pts;
        if (_firstDts[inIdx] == ffmpeg.AV_NOPTS_VALUE && baseDts != ffmpeg.AV_NOPTS_VALUE)
            _firstDts[inIdx] = baseDts;

        // ── 타임스탬프 정규화 ─────────────────────────────────────────
        // 입력 time_base → 출력 time_base 리스케일 + 첫 DTS 기준으로 오프셋 제거
        long offsetDts = _firstDts[inIdx] != ffmpeg.AV_NOPTS_VALUE
            ? _firstDts[inIdx] : 0;

        if (pkt->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            long normalizedPts = pkt->pts - offsetDts;
            pkt->pts = ffmpeg.av_rescale_q_rnd(
                normalizedPts,
                inStream->time_base, outStream->time_base,
                AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
        }

        if (pkt->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            long normalizedDts = pkt->dts - offsetDts;
            pkt->dts = ffmpeg.av_rescale_q_rnd(
                normalizedDts,
                inStream->time_base, outStream->time_base,
                AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
        }

        if (pkt->duration > 0)
            pkt->duration = ffmpeg.av_rescale_q(
                pkt->duration, inStream->time_base, outStream->time_base);

        // 정규화 후 DTS가 음수이면 0으로 클램프 (B프레임 seek 시 발생 가능)
        if (pkt->dts != ffmpeg.AV_NOPTS_VALUE && pkt->dts < 0)
            pkt->dts = 0;
        if (pkt->pts != ffmpeg.AV_NOPTS_VALUE && pkt->dts != ffmpeg.AV_NOPTS_VALUE
            && pkt->pts < pkt->dts)
            pkt->pts = pkt->dts;

        pkt->pos = -1;
        pkt->stream_index = outIdx;

        ret = ffmpeg.av_interleaved_write_frame(outCtx, pkt);

        if (ret < 0)
        {
            // 실패 시: pkt는 소비되지 않았으므로 수동 unref 필요
            ffmpeg.av_packet_unref(pkt);
            _logger.LogWarning("패킷 기록 실패 (무시): {Err}", GetErrorMessage(ret));
        }
        // 성공 시: av_interleaved_write_frame이 내부적으로 pkt를 unref합니다.

        return false;
    }

    /// <summary>
    /// H.264 / HEVC 스트림이 AVCC/HVCC 포맷(MP4)인 경우 Annex B 변환 BSF를 설정합니다.
    /// <para>
    /// MP4 컨테이너는 H.264/HEVC를 길이 접두사(AVCC) 포맷으로 저장합니다.
    /// MKV · TS 등 다른 컨테이너는 시작코드 접두사(Annex B)를 요구합니다.
    /// BSF 없이 그대로 기록하면 av_interleaved_write_frame이 자동으로 실패하여
    /// 헤더(약 293KB)만 기록되고 실제 스트림 데이터는 들어가지 않습니다.
    /// </para>
    /// <para>
    /// extradata[0] == 0x01 은 AVCDecoderConfigurationRecord /
    /// HEVCDecoderConfigurationRecord 의 configurationVersion 바이트로,
    /// AVCC/HVCC 패키징을 의미합니다.
    /// </para>
    /// </summary>
    private unsafe void SetupBitStreamFilters(AVFormatContext* inCtx, AVFormatContext* outCtx)
    {
        int inStreamCount = (int)inCtx->nb_streams;
        _bsfHandles = new nint[inStreamCount]; // 0 = BSF 미사용

        // ── 출력 컨테이너 타입 확인 ───────────────────────────────────
        // MP4/MOV 계열 컨테이너는 내부적으로 H.264/HEVC를 AVCC 포맷으로 저장합니다.
        // AVCC 소스를 AVCC 출력으로 복사할 때 BSF를 적용하면 Annex B로 변환되어
        // avformat_write_header가 실패합니다 (MP4 muxer가 Annex B extradata를 거부).
        //
        // BSF가 필요한 경우: 소스가 AVCC이고 출력이 Annex B 컨테이너 (MKV, TS, AVI 등)
        // BSF가 불필요한 경우: 소스와 출력이 모두 AVCC 계열 (MP4 → MP4)
        string outFmtName = Marshal.PtrToStringUTF8((IntPtr)outCtx->oformat->name) ?? "";
        bool outputIsAvccFamily = outFmtName.Contains("mp4")
                               || outFmtName == "mov"
                               || outFmtName == "m4v"
                               || outFmtName == "3gp"
                               || outFmtName == "3g2"
                               || outFmtName == "f4v"
                               || outFmtName == "ipod"
                               || outFmtName == "ismv";

        if (outputIsAvccFamily)
        {
            _logger.LogDebug("BSF 생략: MP4/MOV 계열 출력 ({FmtName}) — AVCC 그대로 복사", outFmtName);
            return;
        }

        for (int i = 0; i < inStreamCount; i++)
        {
            if (_streamMapping[i] < 0) continue;

            var inStream = inCtx->streams[i];
            var par = inStream->codecpar;

            // AVCC/HVCC 포맷 감지: configurationVersion == 1 (extradata[0] == 0x01)
            // Annex B의 경우 extradata[0] == 0x00 (start code 시작)이므로 미적용
            bool isAvcc = par->extradata != null
                       && par->extradata_size > 1
                       && par->extradata[0] == 0x01;
            if (!isAvcc) continue;

            string? bsfName = par->codec_id switch
            {
                AVCodecID.AV_CODEC_ID_H264 => "h264_mp4toannexb",
                AVCodecID.AV_CODEC_ID_HEVC => "hevc_mp4toannexb",
                _ => null,
            };
            if (bsfName == null) continue;

            var bsfFilter = ffmpeg.av_bsf_get_by_name(bsfName);
            if (bsfFilter == null)
            {
                _logger.LogWarning("BSF '{Name}'를 찾을 수 없습니다.", bsfName);
                continue;
            }

            AVBSFContext* bsfCtx = null;
            int ret = ffmpeg.av_bsf_alloc(bsfFilter, &bsfCtx);
            if (ret < 0 || bsfCtx == null)
            {
                _logger.LogWarning("BSF 컨텍스트 할당 실패: {Err}", GetErrorMessage(ret));
                continue;
            }

            // 입력 코덱 파라미터 + 타임베이스 설정
            ret = ffmpeg.avcodec_parameters_copy(bsfCtx->par_in, par);
            if (ret < 0)
            {
                ffmpeg.av_bsf_free(&bsfCtx);
                _logger.LogWarning("BSF 입력 파라미터 복사 실패: {Err}", GetErrorMessage(ret));
                continue;
            }
            bsfCtx->time_base_in = inStream->time_base;

            ret = ffmpeg.av_bsf_init(bsfCtx);
            if (ret < 0)
            {
                ffmpeg.av_bsf_free(&bsfCtx);
                _logger.LogWarning("BSF 초기화 실패: {Err}", GetErrorMessage(ret));
                continue;
            }

            // BSF 초기화 후 출력 codecpar를 출력 스트림에 반영
            // (Annex B extradata로 갱신됨 — avformat_write_header 전에 반드시 필요)
            int outIdx = _streamMapping[i];
            var outStream = outCtx->streams[outIdx];
            ret = ffmpeg.avcodec_parameters_copy(outStream->codecpar, bsfCtx->par_out);
            if (ret < 0)
            {
                ffmpeg.av_bsf_free(&bsfCtx);
                _logger.LogWarning("BSF 출력 파라미터 복사 실패: {Err}", GetErrorMessage(ret));
                continue;
            }

            _bsfHandles[i] = (nint)bsfCtx;
            _logger.LogInformation(
                "BSF 설정 완료: 스트림 #{Idx} → {Name}", i, bsfName);
        }
    }

    private unsafe void WriteTrailer()
    {
        if (_outputHandle == 0) return;
        var outCtx = (AVFormatContext*)_outputHandle;
        ffmpeg.av_write_trailer(outCtx);
    }

    /// <summary>
    /// 헤더가 기록된 경우에만 트레일러를 기록합니다.
    /// avformat_write_header 없이 av_write_trailer를 호출하면 native crash가 발생합니다.
    /// </summary>
    private void WriteTrailerSafe()
    {
        if (!_headerWritten) return;
        try { WriteTrailer(); }
        catch (Exception ex) { _logger.LogWarning(ex, "트레일러 기록 실패 (무시)"); }
    }

    private unsafe void ReleaseContexts()
    {
        _headerWritten = false;

        // ── BSF 핸들 해제 ──────────────────────────────────────────────
        for (int i = 0; i < _bsfHandles.Length; i++)
        {
            if (_bsfHandles[i] == 0) continue;
            var bsfCtx = (AVBSFContext*)_bsfHandles[i];
            ffmpeg.av_bsf_free(&bsfCtx);
            _bsfHandles[i] = 0;
        }
        _bsfHandles = [];

        if (_packetHandle != 0)
        {
            var p = (AVPacket*)_packetHandle;
            ffmpeg.av_packet_free(&p);
            _packetHandle = 0;
        }

        if (_outputHandle != 0)
        {
            var outCtx = (AVFormatContext*)_outputHandle;
            if (outCtx->oformat != null
                && (outCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0
                && outCtx->pb != null)
            {
                ffmpeg.avio_closep(&outCtx->pb);
            }
            ffmpeg.avformat_free_context(outCtx);
            _outputHandle = 0;
        }

        if (_inputHandle != 0)
        {
            var inCtx = (AVFormatContext*)_inputHandle;
            ffmpeg.avformat_close_input(&inCtx);
            _inputHandle = 0;
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

    // ── IAsyncDisposable ─────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsRecording)
            await StopAsync().ConfigureAwait(false);

        ReleaseContexts();
        _cts?.Dispose();
    }
}
