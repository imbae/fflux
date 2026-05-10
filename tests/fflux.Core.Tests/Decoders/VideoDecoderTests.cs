using System;
using System.IO;
using System.Threading.Tasks;
using fflux.Core.Abstractions;
using fflux.Core.Decoders;
using fflux.Core.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace fflux.Core.Tests.Decoders;

/// <summary>VideoDecoder의 가드 조건과 상태 전이를 검증합니다.</summary>
/// <remarks>
/// FFmpeg 바이너리가 필요한 실제 디코딩 테스트는
/// <see cref="IsFFmpegAvailable"/>로 조건부 실행됩니다.
/// </remarks>
public sealed class VideoDecoderTests
{
    // ── Fixture ──────────────────────────────────────────────────────

    private static IVideoDecoder CreateDecoder(bool isInitialized = true)
    {
        var mock = new Mock<IFFmpegInitializer>();
        mock.Setup(x => x.IsInitialized).Returns(isInitialized);

        return new VideoDecoder(
            mock.Object,
            NullLogger<VideoDecoder>.Instance);
    }

    // ── FFmpeg 미초기화 가드 ─────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_WhenFFmpegNotInitialized_ThrowsInvalidOperation()
    {
        var decoder = CreateDecoder(isInitialized: false);

        var act = async () => await decoder.OpenAsync("any.mp4");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FFmpeg*초기화*");
    }

    // ── 파일 존재 여부 가드 ──────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        var decoder = CreateDecoder();

        var act = async () => await decoder.OpenAsync("nonexistent_xyz.mp4");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task OpenAsync_WhenPathEmpty_ThrowsFileNotFoundException()
    {
        var decoder = CreateDecoder();

        var act = async () => await decoder.OpenAsync(string.Empty);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── DecodeAsync 가드 ─────────────────────────────────────────────

    [Fact]
    public async Task DecodeAsync_WhenNotOpen_ThrowsInvalidOperation()
    {
        var decoder = CreateDecoder();

        var act = async () =>
        {
            // IAsyncEnumerable 이터레이션을 시작해야 예외가 발생합니다.
            await foreach (var _ in decoder.DecodeAsync()) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpenAsync*");
    }

    // ── SeekAsync 가드 ───────────────────────────────────────────────

    [Fact]
    public async Task SeekAsync_WhenNotOpen_ThrowsInvalidOperation()
    {
        var decoder = CreateDecoder();

        var act = async () => await decoder.SeekAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpenAsync*");
    }

    // ── 초기 상태 검증 ──────────────────────────────────────────────

    [Fact]
    public void InitialState_IsOpenFalseAndStreamInfoNull()
    {
        var decoder = CreateDecoder();

        decoder.IsOpen.Should().BeFalse();
        decoder.StreamInfo.Should().BeNull();
        decoder.Duration.Should().Be(TimeSpan.Zero);
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var decoder = CreateDecoder();
        await decoder.DisposeAsync();

        var act = async () => await decoder.OpenAsync("any.mp4");

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── 잘못된 파일 형식 (FFmpeg 가용 시에만 실행) ──────────────────

    [Fact]
    public async Task OpenAsync_WhenFileIsNotMedia_ThrowsMediaReadException()
    {
        if (!IsFFmpegAvailable()) return;  // FFmpeg 미설치 환경에서는 스킵

        var decoder  = CreateDecoder();
        var tempFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFile, "this is not a media file");

            var act = async () => await decoder.OpenAsync(tempFile);

            await act.Should().ThrowAsync<MediaReadException>();
        }
        finally
        {
            File.Delete(tempFile);
            await decoder.DisposeAsync();
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

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
