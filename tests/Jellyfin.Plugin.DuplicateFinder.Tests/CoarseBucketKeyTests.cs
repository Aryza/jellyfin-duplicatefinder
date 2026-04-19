using System;
using Jellyfin.Plugin.DuplicateFinder.Detection;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// The coarse bucket key pre-groups items before the O(k²) fuzzy pass runs.
/// If two items land in different buckets they are never even compared, so
/// every guard we added against cross-series collisions / part-splits /
/// multi-episode ranges must reflect itself in the bucket key.
/// </summary>
public class CoarseBucketKeyTests
{
    // ── Movies ───────────────────────────────────────────────────────────────

    [Fact]
    public void MovieKeyIsFirstWordPlusYearPlusPart()
    {
        var movie = new Movie { Name = "The Matrix", ProductionYear = 1999, Path = "/m/matrix.mkv" };
        Assert.Equal("matrix|1999|P0", DuplicateDetector.CoarseBucketKey(movie));
    }

    [Fact]
    public void MoviesWithDifferentYearsGetDifferentKeys()
    {
        var a = new Movie { Name = "Avatar", ProductionYear = 2009, Path = "/m/a.mkv" };
        var b = new Movie { Name = "Avatar", ProductionYear = 2022, Path = "/m/b.mkv" };
        Assert.NotEqual(
            DuplicateDetector.CoarseBucketKey(a),
            DuplicateDetector.CoarseBucketKey(b));
    }

    [Fact]
    public void MovieWithoutYearUsesQuestionMark()
    {
        var movie = new Movie { Name = "Avatar", Path = "/m/a.mkv" };
        Assert.Equal("avatar|?|P0", DuplicateDetector.CoarseBucketKey(movie));
    }

    [Fact]
    public void MoviePartSplitsGetDifferentKeys()
    {
        var p1 = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/m/Kill Bill Part 1.mkv" };
        var p2 = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/m/Kill Bill Part 2.mkv" };
        Assert.NotEqual(
            DuplicateDetector.CoarseBucketKey(p1),
            DuplicateDetector.CoarseBucketKey(p2));
    }

    // ── Episodes ─────────────────────────────────────────────────────────────

    [Fact]
    public void EpisodeKeyIncludesSeriesSeasonAndEpisode()
    {
        var series = Guid.NewGuid();
        var ep = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/s01e01.mkv"
        };
        Assert.Equal($"ep|jf:{series}|S1E1-E1|P0", DuplicateDetector.CoarseBucketKey(ep));
    }

    [Fact]
    public void EpisodesInDifferentSeriesGetDistinctKeys()
    {
        // Two shows, both S01E01 titled "Pilot" — must NOT share a bucket.
        var a = new Episode
        {
            Name = "Pilot", SeriesId = Guid.NewGuid(),
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/a/s01e01.mkv"
        };
        var b = new Episode
        {
            Name = "Pilot", SeriesId = Guid.NewGuid(),
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/b/s01e01.mkv"
        };
        Assert.NotEqual(
            DuplicateDetector.CoarseBucketKey(a),
            DuplicateDetector.CoarseBucketKey(b));
    }

    [Fact]
    public void MultiEpisodeRangeGetsDistinctKeyFromSingleEpisode()
    {
        var series = Guid.NewGuid();
        var single = new Episode
        {
            SeriesId = series, ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/s01e01.mkv"
        };
        var range = new Episode
        {
            SeriesId = series, ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2,
            Path = "/s01e01-e02.mkv"
        };
        Assert.NotEqual(
            DuplicateDetector.CoarseBucketKey(single),
            DuplicateDetector.CoarseBucketKey(range));
    }

    [Fact]
    public void EpisodePartSplitsGetDistinctKeys()
    {
        var series = Guid.NewGuid();
        var p1 = new Episode
        {
            SeriesId = series, ParentIndexNumber = 2, IndexNumber = 3,
            Path = "/s02/S02E03 Part 1.mkv"
        };
        var p2 = new Episode
        {
            SeriesId = series, ParentIndexNumber = 2, IndexNumber = 3,
            Path = "/s02/S02E03 Part 2.mkv"
        };
        Assert.NotEqual(
            DuplicateDetector.CoarseBucketKey(p1),
            DuplicateDetector.CoarseBucketKey(p2));
    }

    [Fact]
    public void TwoCopiesOfSameEpisodeShareTheSameKey()
    {
        var series = Guid.NewGuid();
        var a = new Episode
        {
            SeriesId = series, ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/libA/s01e01.mkv"
        };
        var b = new Episode
        {
            SeriesId = series, ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/libB/s01e01.mkv"
        };
        Assert.Equal(
            DuplicateDetector.CoarseBucketKey(a),
            DuplicateDetector.CoarseBucketKey(b));
    }

    [Fact]
    public void EpisodeWithoutSeriesIdUsesUnknown()
    {
        // SeriesId defaults to Guid.Empty, ep.Series returns null without
        // hitting LibraryManager — fallback should be "unknown".
        var ep = new Episode
        {
            Name = "Orphan", ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/orphan.mkv"
        };
        Assert.Equal("ep|unknown|S1E1-E1|P0", DuplicateDetector.CoarseBucketKey(ep));
    }
}
