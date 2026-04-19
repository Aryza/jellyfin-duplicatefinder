using Jellyfin.Plugin.DuplicateFinder.Detection;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// Exercises the title-normalisation pipeline used before Jaro-Winkler
/// comparison. This is the layer that makes "The Matrix" and "Matrix" match,
/// or "Avatar: Extended Cut" and "Avatar" match.
/// </summary>
public class NormaliseTitleTests
{
    [Theory]
    [InlineData("Avatar", "avatar")]
    [InlineData("AVATAR", "avatar")]
    [InlineData("  Avatar  ", "avatar")]
    public void LowercasesAndTrims(string input, string expected)
        => Assert.Equal(expected, DuplicateDetector.NormaliseTitle(input));

    [Theory]
    [InlineData("The Matrix", "matrix")]
    [InlineData("A Beautiful Mind", "beautiful mind")]
    [InlineData("An Education", "education")]
    [InlineData("the godfather", "godfather")]
    [InlineData("THE MATRIX", "matrix")]
    public void StripsLeadingArticles(string input, string expected)
        => Assert.Equal(expected, DuplicateDetector.NormaliseTitle(input));

    [Fact]
    public void DoesNotStripArticleInsideTitle()
    {
        // "the" in "Gone with the Wind" must not be stripped — only leading articles.
        Assert.Equal("gone with the wind", DuplicateDetector.NormaliseTitle("Gone with the Wind"));
    }

    [Theory]
    [InlineData("Avatar: Special Edition", "avatar")]
    [InlineData("Avatar - Extended Cut", "avatar")]
    [InlineData("The Matrix: Reloaded", "matrix")]
    [InlineData("Star Trek II: The Wrath of Khan", "star trek ii")]
    public void StripsSubtitlesAfterColonOrDash(string input, string expected)
        => Assert.Equal(expected, DuplicateDetector.NormaliseTitle(input));

    [Theory]
    [InlineData("Avatar", "Avatar: Extended Cut")]
    [InlineData("Matrix", "The Matrix: Reloaded")]
    [InlineData("The Lord of the Rings", "Lord of the Rings - Extended")]
    public void SubtitleAndArticleStrippingProducesSameKey(string a, string b)
    {
        // The whole point of normalisation: these must collapse to the same
        // string so the fuzzy matcher doesn't waste cycles on them.
        Assert.Equal(
            DuplicateDetector.NormaliseTitle(a),
            DuplicateDetector.NormaliseTitle(b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void EmptyOrWhitespaceReturnsEmpty(string input)
        => Assert.Equal(string.Empty, DuplicateDetector.NormaliseTitle(input));

    [Fact]
    public void PunctuationStripped()
    {
        // Apostrophes are removed without inserting a space, so "Ocean's" → "oceans".
        Assert.Equal("oceans eleven", DuplicateDetector.NormaliseTitle("Ocean's Eleven"));
    }

    [Fact]
    public void DigitsArePreserved()
    {
        // "2001" as a title shouldn't become empty.
        Assert.Equal("2001", DuplicateDetector.NormaliseTitle("2001: A Space Odyssey"));
    }
}
