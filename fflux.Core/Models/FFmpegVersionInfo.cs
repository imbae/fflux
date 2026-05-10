namespace fflux.Core.Models;

/// <summary>로드된 FFmpeg 라이브러리 버전 정보입니다.</summary>
public sealed record FFmpegVersionInfo(
    string AvutilVersion,
    string AvcodecVersion,
    string AvformatVersion,
    string SwscaleVersion,
    string SwresampleVersion,
    string BinaryPath
)
{
    /// <summary>빌드에 포함된 FFmpeg 라이브러리 목록과 버전을 요약합니다.</summary>
    public override string ToString() =>
        $"avutil {AvutilVersion} | avcodec {AvcodecVersion} | avformat {AvformatVersion} " +
        $"| swscale {SwscaleVersion} | swresample {SwresampleVersion}";
}
