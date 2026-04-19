using Jellyfin.Plugin.DuplicateFinder.Detection;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// The composite quality score drives which item gets the ★ keep-candidate
/// marker in the report. The formula is:
///     score = (width × height) × 10 + bitrate_kbps + container_bonus + size_mb
/// Container bonus: mkv=200, mp4=100, avi=50.
/// These tests lock in the ranking invariants a user would expect.
/// </summary>
public class QualityScoreTests
{
    [Fact]
    public void UhdOutranksFullHdRegardlessOfContainer()
    {
        var uhd = DuplicateDetector.ComputeQualityScore("3840x2160", 5000, "mp4", 0);
        var fhd = DuplicateDetector.ComputeQualityScore("1920x1080", 5000, "mkv", 0);
        Assert.True(uhd > fhd);
    }

    [Fact]
    public void MkvOutranksMp4WhenEverythingElseEqual()
    {
        var mkv = DuplicateDetector.ComputeQualityScore("1920x1080", 5000, "mkv", 0);
        var mp4 = DuplicateDetector.ComputeQualityScore("1920x1080", 5000, "mp4", 0);
        var avi = DuplicateDetector.ComputeQualityScore("1920x1080", 5000, "avi", 0);
        Assert.True(mkv > mp4);
        Assert.True(mp4 > avi);
    }

    [Fact]
    public void HigherBitrateWinsWhenResolutionAndContainerMatch()
    {
        var high = DuplicateDetector.ComputeQualityScore("1920x1080", 9000, "mkv", 0);
        var low  = DuplicateDetector.ComputeQualityScore("1920x1080", 4000, "mkv", 0);
        Assert.True(high > low);
    }

    [Theory]
    [InlineData("mkv", 200)]
    [InlineData("MKV", 200)]
    [InlineData("mp4", 100)]
    [InlineData("avi", 50)]
    [InlineData("webm", 0)]
    [InlineData(null, 0)]
    public void ContainerBonusMatchesSpec(string? container, long expected)
        => Assert.Equal(expected, DuplicateDetector.ComputeQualityScore(null, 0, container, 0));

    [Fact]
    public void NullResolutionCountsAsZeroPixels()
    {
        // No resolution info → pixels contribute nothing, only bitrate + container.
        var score = DuplicateDetector.ComputeQualityScore(null, 1000, "mkv", 0);
        Assert.Equal(1000 + 200, score);
    }

    [Fact]
    public void MalformedResolutionCountsAsZeroPixels()
    {
        var score = DuplicateDetector.ComputeQualityScore("garbage", 0, null, 0);
        Assert.Equal(0, score);
    }

    [Fact]
    public void FileSizeContributesInMegabytes()
    {
        // 100 MiB → +100 to the score.
        var score = DuplicateDetector.ComputeQualityScore(null, 0, null, 100L * 1024 * 1024);
        Assert.Equal(100, score);
    }

    [Fact]
    public void FullFormulaAgreesWithSpec()
    {
        // 1920×1080 = 2,073,600 pixels × 10 = 20,736,000
        // + 8500 kbps + 200 (mkv) + 4500 MiB ≈ 20,749,200
        var score = DuplicateDetector.ComputeQualityScore("1920x1080", 8500, "mkv", 4500L * 1024 * 1024);
        Assert.Equal(20_736_000L + 8500 + 200 + 4500, score);
    }
}
