using System;
using Jellyfin.Plugin.DuplicateFinder.Detection;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// These tests cover the guard conditions added after a real TV library
/// surfaced false-positive risk: cross-series collisions (two "Pilot"
/// episodes), multi-episode range files, and part-split movies. Each
/// scenario is an explicit regression guard — if any of these return true,
/// the detector will flag unrelated files as duplicates.
/// </summary>
public class MatchesTitleAndYearTests
{
    // ILibraryManager is never consulted inside MatchesTitleAndYear, so null! is safe.
    private readonly DuplicateDetector _detector =
        new(null!, NullLogger<DuplicateDetector>.Instance);

    private const double DefaultThreshold = 0.90;

    // ── Movies ───────────────────────────────────────────────────────────────

    [Fact]
    public void MoviesWithDifferentYearsNeverMatch()
    {
        var a = new Movie { Name = "Avatar", ProductionYear = 2009, Path = "/m/a.mkv" };
        var b = new Movie { Name = "Avatar", ProductionYear = 2022, Path = "/m/b.mkv" };
        Assert.False(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void MoviesWithSameTitleAndYearMatch()
    {
        var a = new Movie { Name = "Avatar", ProductionYear = 2009, Path = "/libA/Avatar.mkv" };
        var b = new Movie { Name = "Avatar", ProductionYear = 2009, Path = "/libB/Avatar.mp4" };
        Assert.True(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void SubtitleVariantMatchesBareTitle()
    {
        var a = new Movie { Name = "Avatar",                 ProductionYear = 2009, Path = "/m/a.mkv" };
        var b = new Movie { Name = "Avatar: Extended Cut",   ProductionYear = 2009, Path = "/m/b.mkv" };
        Assert.True(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void PartSplitMoviesNeverMatchEachOther()
    {
        // Same title, same year, but two halves of one physical movie — not
        // duplicates of each other.
        var part1 = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/m/Kill Bill Part 1.mkv" };
        var part2 = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/m/Kill Bill Part 2.mkv" };
        Assert.False(_detector.MatchesTitleAndYear(part1, part2, DefaultThreshold));
    }

    [Fact]
    public void TwoCopiesOfTheSamePartNumberStillMatch()
    {
        // Two backups of "Part 1" in different library roots are still dupes.
        var a = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/libA/Kill Bill Part 1.mkv" };
        var b = new Movie { Name = "Kill Bill", ProductionYear = 2003, Path = "/libB/Kill Bill Part 1.mkv" };
        Assert.True(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    // ── Episodes ─────────────────────────────────────────────────────────────

    [Fact]
    public void EpisodesFromDifferentSeriesNeverMatchEvenIfTitleSame()
    {
        // The classic false-positive risk: two unrelated shows with S01E01
        // both titled "Pilot". Must be rejected on SeriesId alone.
        var a = new Episode
        {
            Name              = "Pilot",
            SeriesId          = Guid.NewGuid(),
            ParentIndexNumber = 1,
            IndexNumber       = 1,
            Path              = "/series-a/S01E01.mkv"
        };
        var b = new Episode
        {
            Name              = "Pilot",
            SeriesId          = Guid.NewGuid(),
            ParentIndexNumber = 1,
            IndexNumber       = 1,
            Path              = "/series-b/S01E01.mkv"
        };
        Assert.False(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void EpisodesFromSameSeriesWithDifferentEpisodeNumberDoNotMatch()
    {
        var series = Guid.NewGuid();
        var a = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/s01e01.mkv"
        };
        var b = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 2,
            Path = "/s01e02.mkv"
        };
        Assert.False(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void EpisodesFromSameSeriesInDifferentSeasonsDoNotMatch()
    {
        var series = Guid.NewGuid();
        var s1 = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/S01/E01.mkv"
        };
        var s2 = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 2, IndexNumber = 1,
            Path = "/S02/E01.mkv"
        };
        Assert.False(_detector.MatchesTitleAndYear(s1, s2, DefaultThreshold));
    }

    [Fact]
    public void MultiEpisodeRangeDoesNotMatchSingleEpisodeAtSameStart()
    {
        // "S01E01.mkv" (end=1) must not collide with "S01E01-E02.mkv" (end=2).
        var series = Guid.NewGuid();
        var single = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/s01e01.mkv"
        };
        var multi = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2,
            Path = "/s01e01-e02.mkv"
        };
        Assert.False(_detector.MatchesTitleAndYear(single, multi, DefaultThreshold));
    }

    [Fact]
    public void TwoIdenticalMultiEpisodeRangesStillMatch()
    {
        // Two backups of an "E01-E02" file in different libraries should merge.
        var series = Guid.NewGuid();
        var a = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2,
            Path = "/libA/s01e01-e02.mkv"
        };
        var b = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2,
            Path = "/libB/s01e01-e02.mkv"
        };
        Assert.True(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void EpisodesFromSameSeriesWithSameIdentifiersMatch()
    {
        var series = Guid.NewGuid();
        var a = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/libA/s01e01.mkv"
        };
        var b = new Episode
        {
            Name = "Pilot", SeriesId = series,
            ParentIndexNumber = 1, IndexNumber = 1,
            Path = "/libB/s01e01.mkv"
        };
        Assert.True(_detector.MatchesTitleAndYear(a, b, DefaultThreshold));
    }

    [Fact]
    public void EpisodePartSplitsNeverMatchEachOther()
    {
        var series = Guid.NewGuid();
        var p1 = new Episode
        {
            Name = "The Finale", SeriesId = series,
            ParentIndexNumber = 2, IndexNumber = 3,
            Path = "/s02/S02E03 Part 1.mkv"
        };
        var p2 = new Episode
        {
            Name = "The Finale", SeriesId = series,
            ParentIndexNumber = 2, IndexNumber = 3,
            Path = "/s02/S02E03 Part 2.mkv"
        };
        Assert.False(_detector.MatchesTitleAndYear(p1, p2, DefaultThreshold));
    }
}
