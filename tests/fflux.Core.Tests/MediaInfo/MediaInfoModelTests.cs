using System;
using System.Collections.ObjectModel;
using FluentAssertions;
using Xunit;

// 폴더명(MediaInfo)이 모델 클래스명과 충돌하므로 별칭으로 구분합니다.
using MI  = fflux.Core.Models.StreamInfo.MediaInfo;
using VS  = fflux.Core.Models.StreamInfo.VideoStreamInfo;
using AS  = fflux.Core.Models.StreamInfo.AudioStreamInfo;
using SS  = fflux.Core.Models.StreamInfo.SubtitleStreamInfo;

namespace fflux.Core.Tests.MediaInfo;

/// <summary>MediaInfo 관련 모델의 계산 프로퍼티 및 동작을 검증합니다.</summary>
public sealed class MediaInfoModelTests
{
    // ── MediaInfo 편의 프로퍼티 ──────────────────────────────────────

    [Theory]
    [InlineData(1_073_741_824L, "1.00 GB")]
    [InlineData(1_048_576L,     "1.00 MB")]
    [InlineData(1_024L,         "1.0 KB")]
    [InlineData(512L,           "512 B")]
    public void FileSizeText_ReturnsCorrectUnit(long bytes, string expected)
    {
        var info = new MI { FileSize = bytes };
        info.FileSizeText.Should().Be(expected);
    }

    [Fact]
    public void DurationText_Zero_ReturnsNA()
    {
        var info = new MI { Duration = TimeSpan.Zero };
        info.DurationText.Should().Be("N/A");
    }

    [Fact]
    public void DurationText_ReturnsFormattedString()
    {
        var info = new MI { Duration = new TimeSpan(1, 23, 45) };
        info.DurationText.Should().Be("01:23:45");
    }

    [Fact]
    public void PrimaryVideo_WhenEmpty_ReturnsNull()
    {
        new MI().PrimaryVideo.Should().BeNull();
    }

    [Fact]
    public void PrimaryVideo_WhenPresent_ReturnsFirst()
    {
        var video = new VS { StreamIndex = 0, CodecName = "h264" };
        var info  = new MI { VideoStreams = new ReadOnlyCollection<VS>([video]) };
        info.PrimaryVideo.Should().Be(video);
    }

    [Theory]
    [InlineData(0L,          null)]
    [InlineData(5_000_000L,  "5.00 Mbps")]
    [InlineData(10_000_000L, "10.00 Mbps")]
    public void BitRateText_ReturnsExpected(long bps, string? expected)
    {
        new MI { BitRate = bps }.BitRateText.Should().Be(expected);
    }

    // ── VideoStreamInfo ──────────────────────────────────────────────

    [Fact]
    public void VideoStreamInfo_ResolutionText_FormatsCorrectly()
    {
        new VS { Width = 1920, Height = 1080 }
            .ResolutionText.Should().Be("1920×1080");
    }

    [Theory]
    [InlineData(0.0,    "N/A")]
    [InlineData(23.976, "23.98 fps")]
    [InlineData(60.0,   "60.00 fps")]
    public void VideoStreamInfo_FrameRateText_FormatsCorrectly(double fps, string expected)
    {
        new VS { FrameRate = fps }.FrameRateText.Should().Be(expected);
    }

    [Theory]
    [InlineData(0L,          null)]
    [InlineData(5_000_000L,  5.0)]
    [InlineData(10_000_000L, 10.0)]
    public void VideoStreamInfo_BitRateMbps_ReturnsExpected(long bps, double? expected)
    {
        new VS { BitRate = bps }
            .BitRateMbps.Should().BeApproximately(expected, 0.001);
    }

    // ── AudioStreamInfo ──────────────────────────────────────────────

    [Theory]
    [InlineData(48000, "48.0 kHz")]
    [InlineData(44100, "44.1 kHz")]
    [InlineData(22050, "22.1 kHz")]
    public void AudioStreamInfo_SampleRateText_FormatsCorrectly(int hz, string expected)
    {
        new AS { SampleRate = hz }.SampleRateText.Should().Be(expected);
    }

    [Theory]
    [InlineData(0L,       null)]
    [InlineData(128_000L, "128 kbps")]
    [InlineData(320_000L, "320 kbps")]
    public void AudioStreamInfo_BitRateText_ReturnsExpected(long bps, string? expected)
    {
        new AS { BitRate = bps }.BitRateText.Should().Be(expected);
    }

    // ── SubtitleStreamInfo ───────────────────────────────────────────

    [Theory]
    [InlineData("Korean Subtitles", "kor", "Korean Subtitles")] // Title 우선
    [InlineData(null,               "kor", "KOR")]              // 언어 fallback (대문자)
    [InlineData(null,               null,  "Subtitle #3")]      // 인덱스 fallback
    public void SubtitleStreamInfo_DisplayLabel_PrioritizesCorrectly(
        string? title, string? language, string expected)
    {
        new SS { StreamIndex = 3, Title = title, Language = language }
            .DisplayLabel.Should().Be(expected);
    }
}
