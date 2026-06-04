namespace fflux.Core.Abstractions;

/// <summary>
/// 재인코딩 없이 원본 비트스트림을 그대로 복사하여 구간 녹화를 수행하는 인터페이스입니다.
/// Stream Copy 방식이므로 LGPL 안전 — 코덱 라이브러리 링킹이 없습니다.
/// </summary>
public interface IStreamCopyRecorder : IAsyncDisposable
{
    /// <summary>현재 녹화 중이면 true.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// 녹화가 오류로 종료된 경우 오류 메시지. 정상 완료 또는 아직 실행 중이면 null.
    /// </summary>
    string? LastError { get; }

    /// <summary>녹화 경과 시간이 갱신될 때 발생합니다 (약 1초 간격).</summary>
    event EventHandler<TimeSpan>? DurationUpdated;

    /// <summary>
    /// 녹화를 시작합니다.
    /// <paramref name="sourcePath"/>를 새 FormatContext로 열어 <paramref name="outputPath"/>에 스트림 복사합니다.
    /// </summary>
    /// <param name="sourcePath">원본 미디어 파일 경로 또는 스트리밍 URL</param>
    /// <param name="outputPath">출력 파일 경로 (확장자로 컨테이너 자동 결정)</param>
    /// <param name="startTime">녹화 시작 위치 (파일에서 seek 후 복사, 스트림은 무시)</param>
    /// <param name="ct">취소 토큰 — 취소 시 <see cref="StopAsync"/>와 동일하게 정리됩니다</param>
    Task StartAsync(string sourcePath, string outputPath, TimeSpan startTime = default,
                    CancellationToken ct = default);

    /// <summary>
    /// 녹화를 중지하고 파일을 정상 종료합니다 (av_write_trailer).
    /// 반환 후 출력 파일이 완전히 기록되어 있습니다.
    /// </summary>
    Task StopAsync();
}
