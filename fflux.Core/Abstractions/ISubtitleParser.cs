using fflux.Core.Models;

namespace fflux.Core.Abstractions;

/// <summary>자막 파일 파서 인터페이스.</summary>
public interface ISubtitleParser
{
    /// <summary>파일 전체 내용을 받아 <see cref="SubtitleEntry"/> 목록을 반환합니다. (시작 시각 기준 정렬)</summary>
    IReadOnlyList<SubtitleEntry> Parse(string content);
}
