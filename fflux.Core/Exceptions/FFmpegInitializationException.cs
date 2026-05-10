namespace fflux.Core.Exceptions;

/// <summary>FFmpeg 바이너리 로딩 또는 초기화 실패 시 발생하는 예외입니다.</summary>
public sealed class FFmpegInitializationException : Exception
{
    public FFmpegInitializationException(string message)
        : base(message) { }

    public FFmpegInitializationException(string message, Exception inner)
        : base(message, inner) { }
}
