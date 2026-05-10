namespace fflux.Core.Exceptions;

/// <summary>미디어 파일 읽기 또는 스트림 파싱 실패 시 발생하는 예외입니다.</summary>
public sealed class MediaReadException : Exception
{
    /// <summary>FFmpeg 내부 오류 코드 (음수). 해당 없으면 0.</summary>
    public int FFmpegErrorCode { get; }

    public MediaReadException(string message)
        : base(message) { }

    public MediaReadException(string message, int ffmpegErrorCode)
        : base(message)
    {
        FFmpegErrorCode = ffmpegErrorCode;
    }

    public MediaReadException(string message, Exception inner)
        : base(message, inner) { }
}
