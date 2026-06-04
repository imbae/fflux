using fflux.Core.Models;
using fflux.Core.Models.StreamInfo;
using fflux.Core.Models.Options;

namespace fflux.Core.Abstractions;

/// <summary>
/// 비디오 스트림 디코딩 서비스 인터페이스입니다.
/// </summary>
/// <remarks>
/// 사용 흐름:
/// <code>
/// await decoder.OpenAsync(filePath);
/// await foreach (var frame in decoder.DecodeAsync(ct))
///     Render(frame);
/// </code>
/// 재사용 시 <see cref="OpenAsync"/>를 다시 호출하면 기존 컨텍스트가 닫히고 새 파일이 열립니다.
/// </remarks>
public interface IVideoDecoder : IAsyncDisposable
{
    /// <summary>현재 열린 파일의 비디오 스트림 메타데이터. 열기 전이면 <c>null</c>.</summary>
    VideoStreamInfo? StreamInfo { get; }

    /// <summary>파일이 성공적으로 열려 있는 상태인지 여부</summary>
    bool IsOpen { get; }

    /// <summary>영상 전체 길이. 알 수 없으면 <see cref="TimeSpan.Zero"/>.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// 지정한 파일의 비디오 스트림을 열고 디코더를 초기화합니다.
    /// </summary>
    /// <param name="filePath">미디어 파일 경로</param>
    /// <param name="streamIndex">
    ///     사용할 비디오 스트림 인덱스.
    ///     -1이면 FFmpeg가 자동으로 최적 스트림을 선택합니다.
    /// </param>
    /// <param name="options">
    ///     재생 옵션 (공통 디코더 설정 + 스트리밍 전용 설정).
    ///     null이면 기본값을 사용합니다.
    /// </param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">FFmpeg가 초기화되지 않은 경우</exception>
    /// <exception cref="FileNotFoundException">파일이 존재하지 않는 경우</exception>
    /// <exception cref="Exceptions.MediaReadException">스트림 열기 또는 코덱 초기화 실패</exception>
    Task OpenAsync(string filePath, int streamIndex = -1,
                   VideoOpenOptions? options = null,
                   CancellationToken ct = default);

    /// <summary>
    /// 비디오 프레임을 순서대로 디코딩하여 <see cref="VideoFrame"/>으로 반환합니다.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>디코딩은 백그라운드 스레드에서 실행되며 <see cref="Channel{T}"/>로 버퍼링됩니다.</item>
    ///   <item>반환되는 프레임은 항상 BGRA 32-bit 포맷입니다.</item>
    ///   <item><paramref name="ct"/> 취소 시 루프가 안전하게 종료됩니다.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="OpenAsync"/>를 호출하지 않은 경우</exception>
    IAsyncEnumerable<VideoFrame> DecodeAsync(CancellationToken ct = default);

    /// <summary>
    /// 지정한 목표 타임스탬프에 가장 가까운(≤ target) 프레임을 반환합니다.
    /// backward seek 후 목표 PTS를 초과하기 직전까지 전진 디코딩합니다.
    /// 재생 루프가 중단된 상태에서만 호출해야 합니다.
    /// </summary>
    Task<VideoFrame?> SeekAndDecodeAtAsync(TimeSpan target, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="currentPosition"/> 이후의 첫 번째 프레임을 반환합니다.
    /// backward seek → 현재 위치까지 스킵 → 다음 프레임 반환 순서로 동작합니다.
    /// 앞으로 1프레임 이동에 사용합니다.
    /// </summary>
    Task<VideoFrame?> DecodeNextFrameAfterAsync(TimeSpan currentPosition, CancellationToken ct = default);

    /// <summary>
    /// 지정한 위치로 시크합니다. 코덱 버퍼도 함께 플러시됩니다.
    /// </summary>
    /// <param name="position">이동할 시각 (영상 시작 기준)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">디코더가 열려 있지 않은 경우</exception>
    /// <exception cref="Exceptions.MediaReadException">시크 실패</exception>
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);
}
