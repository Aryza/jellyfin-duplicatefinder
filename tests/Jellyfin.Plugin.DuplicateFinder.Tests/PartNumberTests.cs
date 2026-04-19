using Jellyfin.Plugin.DuplicateFinder.Detection;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// The part-number regex is the only safeguard preventing
/// <c>Movie Part 1.mkv</c> and <c>Movie Part 2.mkv</c> from being reported
/// as duplicates of each other. These tests lock the behaviour.
/// </summary>
public class PartNumberTests
{
    [Theory]
    [InlineData("/media/Movie Part 1.mkv", 1)]
    [InlineData("/media/Movie Part 2.mkv", 2)]
    [InlineData("/media/Movie Part 10.mkv", 10)]
    [InlineData("/media/Movie pt1.mkv", 1)]
    [InlineData("/media/Movie pt 3.mkv", 3)]
    [InlineData("/media/Movie PT2.mkv", 2)]
    [InlineData("/media/Movie.CD1.mkv", 1)]
    [InlineData("/media/Movie cd 2.mkv", 2)]
    [InlineData("/media/Movie disc 1.mkv", 1)]
    [InlineData("/media/Movie disk 4.mkv", 4)]
    [InlineData("/media/S01E01 Part 1.mkv", 1)]
    [InlineData("/media/S01E01 Part 2.mkv", 2)]
    public void ExtractsPartNumberFromCommonFormats(string path, int expected)
        => Assert.Equal(expected, DuplicateDetector.PartNumber(path));

    [Theory]
    [InlineData("/media/Movie.mkv")]
    [InlineData("/media/S01E01.mkv")]
    [InlineData("/media/Avatar (2009).mkv")]
    public void ReturnsZeroWhenNoPartMarker(string path)
        => Assert.Equal(0, DuplicateDetector.PartNumber(path));

    [Fact]
    public void ReturnsZeroForNullPath()
        => Assert.Equal(0, DuplicateDetector.PartNumber(null));

    [Fact]
    public void ReturnsZeroForEmptyPath()
        => Assert.Equal(0, DuplicateDetector.PartNumber(string.Empty));

    [Fact]
    public void DirectoryNameDoesNotLeakIntoPartDetection()
    {
        // The regex only runs on the filename (minus extension). A parent
        // directory called "Part 1 Collection" must not set part=1 on files
        // inside it.
        Assert.Equal(0, DuplicateDetector.PartNumber("/Media/Part 1 Collection/Movie.mkv"));
    }

    [Fact]
    public void DoesNotMisfireOnChapterOrSeason()
    {
        // Neither keyword appears in the regex alternation — must return 0.
        Assert.Equal(0, DuplicateDetector.PartNumber("/media/Chapter 3.mkv"));
        Assert.Equal(0, DuplicateDetector.PartNumber("/media/Season 2 Disc.mkv"));
    }

    [Fact]
    public void DoesNotMatchPartWithoutTrailingDigitBoundary()
    {
        // "Party" contains "part" but is not followed by a digit; must not match.
        Assert.Equal(0, DuplicateDetector.PartNumber("/media/The Party Movie.mkv"));
    }
}
