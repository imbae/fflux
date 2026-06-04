using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>자막 항목 목록을 특정 포맷의 문자열로 직렬화합니다.</summary>
public interface ISubtitleSerializer
{
    /// <summary>
    /// 자막 목록을 직렬화하여 파일에 쓸 수 있는 문자열을 반환합니다.
    /// 시작 시각 기준으로 정렬하여 출력합니다.
    /// </summary>
    string Serialize(IEnumerable<SubtitleEntry> entries);
}
