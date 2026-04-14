using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.DuplicateFinder.Detection;

/// <summary>
/// Describes a single media file that is part of a duplicate group,
/// enriched with media info for the user to make a keep/delete decision.
/// </summary>
public class DuplicateItem
{
    /// <summary>Jellyfin internal item ID.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Display name as shown in the Jellyfin UI.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Full path to the media file on disk.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>File size in bytes. -1 if unavailable.</summary>
    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; init; }

    // ── Media stream info ────────────────────────────────────────────────────

    /// <summary>e.g. "1920x1080", "3840x2160". Null if not available.</summary>
    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    /// <summary>Video codec, e.g. "hevc", "h264", "av1".</summary>
    [JsonPropertyName("videoCodec")]
    public string? VideoCodec { get; init; }

    /// <summary>Overall container bitrate in kbps. 0 if unknown.</summary>
    [JsonPropertyName("bitRateKbps")]
    public int BitRateKbps { get; init; }

    /// <summary>Container format, e.g. "mkv", "mp4".</summary>
    [JsonPropertyName("container")]
    public string? Container { get; init; }

    // ── Provider IDs that caused this match ──────────────────────────────────

    /// <summary>TMDb ID if present on this item.</summary>
    [JsonPropertyName("tmdbId")]
    public string? TmdbId { get; init; }

    /// <summary>IMDB ID if present on this item.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; init; }

    /// <summary>MusicBrainz track ID if present on this item.</summary>
    [JsonPropertyName("musicBrainzId")]
    public string? MusicBrainzId { get; init; }

    // ── Quality score ────────────────────────────────────────────────────────

    /// <summary>
    /// Computed quality score (higher = better). Used to rank versions within
    /// a group so the UI can suggest which copy to keep.
    /// Scoring: resolution pixels (weighted) + bitrate + container bonus.
    /// </summary>
    [JsonPropertyName("qualityScore")]
    public long QualityScore { get; init; }
}

/// <summary>
/// A group of two or more items that the detector considers duplicates.
/// </summary>
public class DuplicateGroup
{
    /// <summary>How the group was identified.</summary>
    [JsonPropertyName("reason")]
    public MatchReason Reason { get; init; }

    /// <summary>
    /// Human-readable label for the group, e.g. "Avatar (2009)" or
    /// "S01E03 - The One With…".
    /// </summary>
    [JsonPropertyName("groupLabel")]
    public string GroupLabel { get; init; } = string.Empty;

    /// <summary>All items in this duplicate group, sorted best-first by QualityScore.</summary>
    [JsonPropertyName("items")]
    public List<DuplicateItem> Items { get; init; } = new();

    /// <summary>Convenience: the item with the highest QualityScore (suggested keep).</summary>
    [JsonIgnore]
    public DuplicateItem? BestCandidate => Items.Count > 0 ? Items[0] : null;
}

/// <summary>Reason a duplicate group was formed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchReason
{
    /// <summary>Two items share a TMDb / IMDB / MusicBrainz provider ID.</summary>
    ProviderId,

    /// <summary>Two items match on normalised title + production year via fuzzy comparison.</summary>
    TitleAndYear,

    /// <summary>Both provider ID and title+year match (strongest signal).</summary>
    Both
}
