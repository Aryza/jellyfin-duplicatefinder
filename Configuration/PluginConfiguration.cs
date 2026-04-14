using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DuplicateFinder.Configuration;

/// <summary>
/// User-configurable settings, persisted as XML by Jellyfin's plugin framework.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ── Scope ────────────────────────────────────────────────────────────────

    /// <summary>Include movies in duplicate scanning.</summary>
    public bool ScanMovies { get; set; } = true;

    /// <summary>Include TV episodes in duplicate scanning.</summary>
    public bool ScanEpisodes { get; set; } = true;

    /// <summary>Include music tracks in duplicate scanning.</summary>
    public bool ScanMusic { get; set; } = true;

    // ── Matching ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum Jaro-Winkler similarity (0.0–1.0) for two titles to be considered
    /// a fuzzy match. 0.90 catches "Avatar" / "Avatar: The Way of Water" while
    /// rejecting clearly different titles.
    /// </summary>
    public double FuzzyMatchThreshold { get; set; } = 0.90;

    /// <summary>
    /// When true, two items with matching provider IDs (TMDb, IMDB, MusicBrainz)
    /// are always flagged as duplicates regardless of fuzzy score.
    /// </summary>
    public bool MatchOnProviderIds { get; set; } = true;

    /// <summary>
    /// When true, title + year fuzzy matching is used as a fallback (or primary
    /// signal when provider IDs are absent).
    /// </summary>
    public bool MatchOnTitleAndYear { get; set; } = true;

    /// <summary>
    /// When true, media stream properties (resolution, codec) are included in
    /// the duplicate report to help you decide which copy to keep.
    /// </summary>
    public bool IncludeMediaInfo { get; set; } = true;
}
