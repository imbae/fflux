using fflux.Core.Exceptions;
using fflux.Core.Models.StreamInfo;

namespace fflux.Core.Abstractions;

/// <summary>
/// 미디어 파일을 열고 스트림 정보를 추출하는 서비스 인터페이스입니다.
/// </summary>
public interface IMediaFileReader
{
    /// <summary>
    /// 미디어 파일을 열고 포맷 및 스트림 정보를 비동기적으로 추출합니다.
    /// </summary>
    /// <param name="filePath">읽을 미디어 파일의 전체 경로</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>파싱된 <see cref="MediaInfo"/></returns>
    /// <exception cref="System.IO.FileNotFoundException">파일이 존재하지 않는 경우</exception>
    /// <exception cref="MediaReadException">FFmpeg가 파일을 열거나 스트림 정보를 읽는 데 실패한 경우</exception>
    /// <exception cref="InvalidOperationException">FFmpeg가 초기화되지 않은 경우</exception>
    Task<MediaInfo> ReadAsync(string filePath, CancellationToken ct = default);
}
