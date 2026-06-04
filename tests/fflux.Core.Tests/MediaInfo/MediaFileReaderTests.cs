using System;
using System.IO;
using System.Threading.Tasks;
using fflux.Core.Abstractions;
using fflux.Core.Demuxers;
using fflux.Core.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace fflux.Core.Tests.MediaInfo;

/// <summary>
/// MediaFileReader의 입력 검증 및 예외 처리를 검증합니다.
/// </summary>
/// <remarks>
/// FFmpeg 바이너리가 필요한 실제 디코딩 테스트는
/// <see cref="SkipIfFFmpegNotInitialized"/>로 조건부 실행됩니다.
/// </remarks>
public sealed class MediaFileReaderTests
{
    // ── Fixture ──────────────────────────────────────────────────────

    private static IMediaFileReader CreateReader(bool isInitialized = true)
    {
        var initializerMock = new Mock<IFFmpegInitializer>();
        initializerMock.Setup(x => x.IsInitialized).Returns(isInitialized);

        return new MediaFileReader(
            initializerMock.Object,
            NullLogger<MediaFileReader>.Instance);
    }

    // ── FFmpeg 미초기화 상태 ─────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WhenFFmpegNotInitialized_ThrowsInvalidOperation()
    {
        var reader = CreateReader(isInitialized: false);

        var act = async () => await reader.ReadAsync("anyfile.mp4");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FFmpeg*초기화*");
    }

    // ── 파일 존재 여부 검증 ──────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        var reader = CreateReader();

        var act = async () => await reader.ReadAsync("nonexistent_file_xyz.mp4");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_WhenPathIsEmpty_ThrowsFileNotFoundException()
    {
        var reader = CreateReader();

        // File.Exists("") returns false → FileNotFoundException
        var act = async () => await reader.ReadAsync(string.Empty);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── 잘못된 파일 형식 ─────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WhenFileIsNotMedia_ThrowsMediaReadException()
    {
        // Arrange: FFmpeg가 초기화된 상태를 시뮬레이션
        // 실제 FFmpeg 바이너리 없이는 DllNotFoundException이 발생하므로,
        // 이 테스트는 FFmpeg가 로드된 환경에서만 의미가 있습니다.
        var skip = !IsFFmpegAvailable();
        if (skip)
        {
            // FFmpeg 미설치 환경에서는 스킵 (테스트 실패 아님)
            return;
        }

        var reader = CreateReader();

        // 빈 텍스트 파일을 미디어로 열면 FFmpeg가 거부합니다.
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "this is not a media file");

            var act = async () => await reader.ReadAsync(tempFile);

            await act.Should().ThrowAsync<MediaReadException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    /// <summary>
    /// FFmpeg 바이너리가 현재 프로세스에 로드되어 있는지 확인합니다.
    /// 통합 테스트 조건부 실행에 사용됩니다.
    /// </summary>
    private static bool IsFFmpegAvailable()
    {
        try
        {
            _ = ffmpeg.avutil_version();
            return true;
        }
        catch (Exception)
        {
            // DllNotFoundException  — DLL not on PATH
            // NotSupportedException — DynamicallyLoadedBindings not initialized
            return false;
        }
    }
}
