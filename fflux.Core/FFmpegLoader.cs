using fflux.Core.Exceptions;
using fflux.Core.Models;

namespace fflux.Core;

/// <summary>
/// FFmpeg LGPL 바이너리를 지정된 폴더에서 로드하는 내부 헬퍼입니다.
/// </summary>
/// <remarks>
/// ffmpeg.autogen의 <c>ffmpeg.RootPath</c>를 설정하면 이후 FFmpeg API 호출 시
/// 해당 경로의 DLL이 지연 로딩됩니다.
/// <para>
/// LGPL 필수 라이브러리: avutil, avcodec, avformat, swscale, swresample
/// </para>
/// </remarks>
internal static class FFmpegLoader
{
    // LGPL 빌드에 필요한 라이브러리 접두사 목록
    private static readonly string[] RequiredLibPrefixes =
        ["avutil", "avcodec", "avformat", "swscale", "swresample"];

    // ── 공개 진입점 ───────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="binaryPath"/>에서 FFmpeg 바이너리를 로드하고
    /// 버전 정보를 반환합니다.
    /// </summary>
    /// <exception cref="FFmpegInitializationException">경로 오류 또는 DLL 로딩 실패 시</exception>
    internal static FFmpegVersionInfo Load(string binaryPath)
    {
        ValidateDirectory(binaryPath);

        try
        {
            // ffmpeg.autogen은 RootPath 설정 후 첫 API 호출 시 DLL을 지연 로드합니다.
            ffmpeg.RootPath = binaryPath;

            // 각 라이브러리를 실제로 호출하여 로딩을 유발하고 버전을 수집합니다.
            var avutil     = UnpackVersion(ffmpeg.avutil_version());
            var avcodec    = UnpackVersion(ffmpeg.avcodec_version());
            var avformat   = UnpackVersion(ffmpeg.avformat_version());
            var swscale    = UnpackVersion(ffmpeg.swscale_version());
            var swresample = UnpackVersion(ffmpeg.swresample_version());

            return new FFmpegVersionInfo(
                avutil, avcodec, avformat, swscale, swresample, binaryPath);
        }
        catch (DllNotFoundException ex)
        {
            throw new FFmpegInitializationException(
                $"FFmpeg LGPL 바이너리를 찾을 수 없습니다.\n" +
                $"경로: {binaryPath}\n" +
                $"필요 DLL: {string.Join(", ", RequiredLibPrefixes)}-XX.dll\n" +
                $"원인: {ex.Message}",
                ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new FFmpegInitializationException(
                $"FFmpeg 바이너리 아키텍처가 맞지 않습니다. 64비트(x64) 빌드가 필요합니다.\n" +
                $"경로: {binaryPath}",
                ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new FFmpegInitializationException(
                $"FFmpeg DLL 버전이 ffmpeg.autogen 8.x와 호환되지 않습니다.\n" +
                $"FFmpeg 6.x 또는 7.x LGPL 빌드를 사용해야 합니다.",
                ex);
        }
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────

    /// <summary>경로가 존재하고 필수 DLL 파일이 있는지 사전 검증합니다.</summary>
    private static void ValidateDirectory(string binaryPath)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
            throw new FFmpegInitializationException(
                "FFmpeg 바이너리 경로가 설정되지 않았습니다.\n" +
                "설정(Settings)에서 FFmpeg 폴더 경로를 지정해 주세요.");

        if (!Directory.Exists(binaryPath))
            throw new FFmpegInitializationException(
                $"지정된 FFmpeg 경로가 존재하지 않습니다: {binaryPath}");

        // 필수 DLL 접두사 중 하나라도 없으면 조기에 오류를 보고합니다.
        var missing = RequiredLibPrefixes
            .Where(prefix => !Directory
                .EnumerateFiles(binaryPath, $"{prefix}-*.dll")
                .Any())
            .ToList();

        if (missing.Count > 0)
            throw new FFmpegInitializationException(
                $"FFmpeg LGPL 필수 라이브러리를 찾을 수 없습니다: {string.Join(", ", missing)}\n" +
                $"경로: {binaryPath}\n" +
                $"ffmpeg.org 에서 LGPL 빌드를 다운로드하세요.");
    }

    /// <summary>ffmpeg.autogen이 반환하는 uint 버전 값을 "major.minor.patch" 문자열로 변환합니다.</summary>
    private static string UnpackVersion(uint version)
    {
        var major = (version >> 16) & 0xFF;
        var minor = (version >>  8) & 0xFF;
        var patch =  version        & 0xFF;
        return $"{major}.{minor}.{patch}";
    }
}
