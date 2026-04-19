using Jellyfin.Plugin.DuplicateFinder.Detection;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// Sanity-checks the Jaro-Winkler implementation. We don't verify exact
/// scores to 6 decimal places — instead we assert invariants (identity,
/// zero for disjoint, prefix boost) and use generous ranges for known pairs.
/// </summary>
public class JaroWinklerTests
{
    [Fact]
    public void IdenticalStringsReturn1()
    {
        Assert.Equal(1.0, DuplicateDetector.JaroWinkler("avatar", "avatar"));
    }

    [Fact]
    public void SingleCharacterSwapReturnsNearOne()
    {
        // Wikipedia's classic JW example: "martha" vs "marhta" ≈ 0.961
        var score = DuplicateDetector.JaroWinkler("martha", "marhta");
        Assert.InRange(score, 0.95, 1.0);
    }

    [Fact]
    public void SubstantialEditDistanceScoresLower()
    {
        // Still related but less so.
        var score = DuplicateDetector.JaroWinkler("dixon", "dicksonx");
        Assert.InRange(score, 0.75, 0.90);
    }

    [Fact]
    public void CompletelyDisjointStringsReturnZero()
    {
        Assert.Equal(0.0, DuplicateDetector.JaroWinkler("xyz", "abc"));
    }

    [Fact]
    public void TwoEmptyStringsAreIdentical()
    {
        // Two empty strings are identical → 1.0 (the equality shortcut fires).
        Assert.Equal(1.0, DuplicateDetector.JaroWinkler(string.Empty, string.Empty));
    }

    [Fact]
    public void CommonPrefixBoostsScoreOverSameCharactersWithoutPrefix()
    {
        // "abcde" vs "abcdf" shares a 4-char prefix → Winkler prefix boost applies.
        // "eabcd" vs "fabcd" has the same set of differences but no common prefix.
        var withPrefix    = DuplicateDetector.JaroWinkler("abcde", "abcdf");
        var withoutPrefix = DuplicateDetector.JaroWinkler("eabcd", "fabcd");
        Assert.True(
            withPrefix > withoutPrefix,
            $"Expected prefix-boosted score {withPrefix} > {withoutPrefix}");
    }

    [Fact]
    public void AvatarVariantsClearDefaultThreshold()
    {
        // After NormaliseTitle, "Avatar" and "Avatar 2" would become "avatar" / "avatar 2".
        // Confirm they still clear the default 0.90 threshold.
        var score = DuplicateDetector.JaroWinkler("avatar", "avatar 2");
        Assert.True(score >= 0.90, $"JW(avatar, avatar 2) = {score} < 0.90");
    }
}
