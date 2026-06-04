using fflux.Core.Models;
using fflux.Core.Models.StreamInfo;

namespace fflux.Core.Abstractions;

/// <summary>
/// 오디오 스트림 디코딩 및 PCM 변환 서비스 인터페이스입니다.
/// </summary>
/// <remarks>
/// 사용 흐름:
/// <code>
/// await decoder.OpenAsync(filePath);
/// await foreach (var frame in decoder.DecodeAsync(ct))
///     PlayPcm(frame);
/// </code>
/// 재사용 시 <see cref="OpenAsync"/>를 다시 호출하면 기존 컨텍스트가 닫히고 새 파일이 열립니다.
/// </remarks>
public interface IAudioDecoder : IAsyncDisposable
{
    /// <summary>현재 열린 파일의 오디오 스트림 메타데이터. 열기 전이면 <c>null</c>.</summary>
    AudioStreamInfo? StreamInfo { get; }

    /// <summary>파일이 성공적으로 열려 있는 상태인지 여부</summary>
    bool IsOpen { get; }

    /// <summary>오디오 전체 길이. 알 수 없으면 <see cref="TimeSpan.Zero"/>.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// 지정한 파일의 오디오 스트림을 열고 디코더를 초기화합니다.
    /// </summary>
    /// <param name="filePath">미디어 파일 경로</param>
    /// <param name="streamIndex">
    ///     사용할 오디오 스트림 인덱스.
    ///     -1이면 FFmpeg가 자동으로 최적 스트림을 선택합니다.
    /// </param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">FFmpeg가 초기화되지 않은 경우</exception>
    /// <exception cref="FileNotFoundException">파일이 존재하지 않는 경우</exception>
    /// <exception cref="Exceptions.MediaReadException">스트림 열기 또는 코덱 초기화 실패</exception>
    Task OpenAsync(string filePath, int streamIndex = -1, CancellationToken ct = default);

    /// <summary>
    /// 오디오 프레임을 순서대로 디코딩하여 IEEE float PCM <see cref="AudioFrame"/>으로 반환합니다.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>디코딩은 백그라운드 스레드에서 실행되며 <see cref="Channel{T}"/>로 버퍼링됩니다.</item>
    ///   <item>반환 샘플은 항상 인터리브 IEEE float 32-bit 포맷입니다.</item>
    ///   <item><paramref name="ct"/> 취소 시 루프가 안전하게 종료됩니다.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="OpenAsync"/>를 호출하지 않은 경우</exception>
    IAsyncEnumerable<AudioFrame> DecodeAsync(CancellationToken ct = default);

    /// <summary>
    /// 지정한 위치로 시크합니다. 코덱 버퍼도 함께 플러시됩니다.
    /// </summary>
    /// <param name="position">이동할 시각 (음성 시작 기준)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">디코더가 열려 있지 않은 경우</exception>
    /// <exception cref="Exceptions.MediaReadException">시크 실패</exception>
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);
}
