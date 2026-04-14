using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DuplicateFinder.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DuplicateFinder.Detection;

/// <summary>Progress snapshot reported during a scan.</summary>
public class ScanProgress
{
    public ScanPhase Phase           { get; init; }
    public string    CurrentItem     { get; init; } = string.Empty;
    public int       ItemsProcessed  { get; init; }
    public int       ItemsTotal      { get; init; }
    public int       PercentComplete { get; init; }
}

public enum ScanPhase { Fetching, Comparing, Done }

/// <summary>
/// Main detection engine. Stateless — call <see cref="FindDuplicates"/> to run a scan.
///
/// Performance strategy: instead of O(n²) brute-force pair comparison, items are
/// pre-grouped into "candidate buckets" by cheap keys (provider ID, normalised title+year).
/// Only items within the same bucket are compared against each other, reducing the
/// effective comparison count from ~15M (5500 items) to a few thousand.
/// </summary>
public class DuplicateDetector
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<DuplicateDetector> _logger;

    public DuplicateDetector(ILibraryManager libraryManager, ILogger<DuplicateDetector> logger)
    {
        _libraryManager = libraryManager;
        _logger         = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public IReadOnlyList<DuplicateGroup> FindDuplicates(
        PluginConfiguration config,
        CancellationToken cancellationToken,
        Action<ScanProgress>? onProgress = null)
    {
        // Phase 1 – fetch (one call per type to avoid EF multi-collection join warning)
        onProgress?.Invoke(new ScanProgress
        {
            Phase = ScanPhase.Fetching, CurrentItem = "Loading library items…",
            ItemsProcessed = 0, ItemsTotal = 0, PercentComplete = 0
        });

        var items = FetchItems(config, cancellationToken);
        int total = items.Count;
        _logger.LogInformation("DuplicateFinder: fetched {Count} items for scanning", total);

        // Phase 2 – build candidate buckets and compare within them
        var uf         = new UnionFind<Guid>();
        var pairReasons = new Dictionary<(Guid, Guid), MatchReason>();

        foreach (var item in items)
            uf.MakeSet(item.Id);

        int processed = 0;

        // ── 2a. Provider ID buckets (zero false positives, very fast) ─────────
        if (config.MatchOnProviderIds)
        {
            processed = MergeByProviderBuckets(items, uf, pairReasons, cancellationToken,
                onProgress, processed, total);
        }

        // ── 2b. Title + year buckets (fuzzy, only within same coarse bucket) ──
        if (config.MatchOnTitleAndYear)
        {
            processed = MergeByTitleBuckets(items, config, uf, pairReasons, cancellationToken,
                onProgress, processed, total);
        }

        onProgress?.Invoke(new ScanProgress
        {
            Phase = ScanPhase.Done, CurrentItem = string.Empty,
            ItemsProcessed = total, ItemsTotal = total, PercentComplete = 100
        });

        // Phase 3 – collect groups
        var results = items
            .GroupBy(i => uf.Find(i.Id))
            .Where(g => g.Count() > 1)
            .Select(cluster =>
            {
                var clusterItems = cluster
                    .Select(i => BuildDuplicateItem(i, config))
                    .OrderByDescending(d => d.QualityScore)
                    .ToList();

                var clusterIds = new HashSet<Guid>(cluster.Select(i => i.Id));
                var reason = pairReasons
                    .Where(kvp => clusterIds.Contains(kvp.Key.Item1) || clusterIds.Contains(kvp.Key.Item2))
                    .Select(kvp => kvp.Value)
                    .DefaultIfEmpty(MatchReason.TitleAndYear)
                    .Max();

                return new DuplicateGroup
                {
                    GroupLabel = BuildGroupLabel(cluster.First()),
                    Reason     = reason,
                    Items      = clusterItems
                };
            })
            .OrderBy(g => g.GroupLabel)
            .ToList();

        _logger.LogInformation("DuplicateFinder: found {Count} duplicate groups", results.Count);
        return results;
    }

    // ── Bucket-based merging ──────────────────────────────────────────────────

    /// <summary>
    /// Groups items by each provider ID they carry, then union-finds within each group.
    /// A single pass over all items builds the buckets; no O(n²) loop needed.
    /// </summary>
    private int MergeByProviderBuckets(
        List<BaseItem> items,
        UnionFind<Guid> uf,
        Dictionary<(Guid, Guid), MatchReason> pairReasons,
        CancellationToken ct,
        Action<ScanProgress>? onProgress,
        int processed,
        int total)
    {
        // Bucket key → list of item IDs that share it
        var tmdbBuckets   = new Dictionary<string, List<Guid>>();
        var imdbBuckets   = new Dictionary<string, List<Guid>>();
        var mbBuckets     = new Dictionary<string, List<Guid>>();
        var episodeBuckets = new Dictionary<string, List<Guid>>(); // seriesTmdb:S:E

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            var tmdb = item.GetProviderId(MetadataProvider.Tmdb);
            if (!string.IsNullOrEmpty(tmdb))
                Bucket(tmdbBuckets, tmdb, item.Id);

            var imdb = item.GetProviderId(MetadataProvider.Imdb);
            if (!string.IsNullOrEmpty(imdb))
                Bucket(imdbBuckets, imdb, item.Id);

            var mb = item.GetProviderId(MetadataProvider.MusicBrainzTrack);
            if (!string.IsNullOrEmpty(mb))
                Bucket(mbBuckets, mb, item.Id);

            if (item is Episode ep && ep.IndexNumber.HasValue && ep.ParentIndexNumber.HasValue)
            {
                var seriesId = ep.GetProviderId(MetadataProvider.Tmdb)
                            ?? ep.Series?.GetProviderId(MetadataProvider.Tmdb);
                if (!string.IsNullOrEmpty(seriesId))
                {
                    var key = $"{seriesId}:S{ep.ParentIndexNumber}E{ep.IndexNumber}";
                    Bucket(episodeBuckets, key, item.Id);
                }
            }

            processed++;
            if (processed % 200 == 0)
            {
                var fileName = System.IO.Path.GetFileName(item.Path) ?? item.Name ?? string.Empty;
                onProgress?.Invoke(new ScanProgress
                {
                    Phase = ScanPhase.Comparing, CurrentItem = fileName,
                    ItemsProcessed = processed, ItemsTotal = total,
                    PercentComplete = total > 0 ? processed * 50 / total : 0 // provider pass = 0–50%
                });
            }
        }

        MergeBuckets(tmdbBuckets,    uf, pairReasons, MatchReason.ProviderId);
        MergeBuckets(imdbBuckets,    uf, pairReasons, MatchReason.ProviderId);
        MergeBuckets(mbBuckets,      uf, pairReasons, MatchReason.ProviderId);
        MergeBuckets(episodeBuckets, uf, pairReasons, MatchReason.ProviderId);

        return processed;
    }

    /// <summary>
    /// Groups items into coarse title+year buckets, then does O(k²) fuzzy comparison
    /// only within each bucket. Bucket key = normalised first word + production year.
    /// </summary>
    private int MergeByTitleBuckets(
        List<BaseItem> items,
        PluginConfiguration config,
        UnionFind<Guid> uf,
        Dictionary<(Guid, Guid), MatchReason> pairReasons,
        CancellationToken ct,
        Action<ScanProgress>? onProgress,
        int processed,
        int total)
    {
        // Group by type first (we never compare movies against episodes)
        var byType = items.GroupBy(i => i.GetType());

        foreach (var typeGroup in byType)
        {
            var typeItems = typeGroup.ToList();

            // Coarse bucket: normalised first word + year (or "unknown")
            var buckets = typeItems
                .GroupBy(i => CoarseBucketKey(i))
                .Where(g => g.Count() > 1); // single-item buckets can't have duplicates

            foreach (var bucket in buckets)
            {
                ct.ThrowIfCancellationRequested();

                var candidates = bucket.ToList();

                // O(k²) within bucket — k is typically 2–5
                for (int i = 0; i < candidates.Count; i++)
                {
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var a = candidates[i];
                        var b = candidates[j];

                        if (!MatchesTitleAndYear(a, b, config.FuzzyMatchThreshold))
                            continue;

                        var rootA = uf.Find(a.Id);
                        var rootB = uf.Find(b.Id);
                        uf.Union(a.Id, b.Id);

                        // Don't downgrade an existing ProviderId match
                        var key = rootA.CompareTo(rootB) < 0 ? (rootA, rootB) : (rootB, rootA);
                        if (!pairReasons.TryGetValue(key, out var existing))
                            pairReasons[key] = MatchReason.TitleAndYear;
                        else if (existing == MatchReason.ProviderId)
                            pairReasons[key] = MatchReason.Both;
                    }
                }

                processed += candidates.Count;
                var sample = System.IO.Path.GetFileName(candidates[0].Path)
                          ?? candidates[0].Name ?? string.Empty;
                onProgress?.Invoke(new ScanProgress
                {
                    Phase = ScanPhase.Comparing, CurrentItem = sample,
                    ItemsProcessed = Math.Min(processed, total), ItemsTotal = total,
                    PercentComplete = total > 0
                        ? 50 + Math.Min(processed, total) * 50 / total  // title pass = 50–100%
                        : 99
                });
            }
        }

        return processed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Bucket<TKey>(Dictionary<TKey, List<Guid>> dict, TKey key, Guid id)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
            dict[key] = list = new List<Guid>();
        list.Add(id);
    }

    private static void MergeBuckets(
        Dictionary<string, List<Guid>> buckets,
        UnionFind<Guid> uf,
        Dictionary<(Guid, Guid), MatchReason> pairReasons,
        MatchReason reason)
    {
        foreach (var bucket in buckets.Values)
        {
            if (bucket.Count < 2) continue;
            // Union everything in the bucket with the first element as anchor
            for (int i = 1; i < bucket.Count; i++)
            {
                var rootA = uf.Find(bucket[0]);
                var rootB = uf.Find(bucket[i]);
                uf.Union(bucket[0], bucket[i]);

                var key = rootA.CompareTo(rootB) < 0 ? (rootA, rootB) : (rootB, rootA);
                if (!pairReasons.TryGetValue(key, out var existing) || reason > existing)
                    pairReasons[key] = reason;
            }
        }
    }

    /// <summary>
    /// Coarse bucket key: normalised first word + production year.
    /// "Avatar: Special Edition (2009)" → "avatar|2009"
    /// "The Matrix Reloaded (2003)"     → "matrix|2003"
    /// Groups items that could plausibly be duplicates into the same bucket
    /// without any false-negative risk (different first words → never duplicates
    /// after subtitle stripping, which is true for the vast majority of titles).
    /// </summary>
    private static string CoarseBucketKey(BaseItem item)
    {
        var title = NormaliseTitle(item.Name ?? string.Empty);
        var word  = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? title;
        var year  = item.ProductionYear?.ToString() ?? "?";

        // For episodes, add season+episode to keep buckets tight
        if (item is Episode ep)
            return $"{word}|{year}|S{ep.ParentIndexNumber}E{ep.IndexNumber}";

        return $"{word}|{year}";
    }

    // ── Item fetching ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches one item type per query to avoid the EF MultipleCollectionInclude
    /// warning that appears when Jellyfin joins multiple navigations in one query.
    /// </summary>
    private List<BaseItem> FetchItems(PluginConfiguration config, CancellationToken ct)
    {
        var items = new List<BaseItem>();

        if (config.ScanMovies)
        {
            ct.ThrowIfCancellationRequested();
            items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem    = false,
                Recursive        = true
            }));
        }

        if (config.ScanEpisodes)
        {
            ct.ThrowIfCancellationRequested();
            items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsVirtualItem    = false,
                Recursive        = true
            }));
        }

        if (config.ScanMusic)
        {
            ct.ThrowIfCancellationRequested();
            items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Audio },
                IsVirtualItem    = false,
                Recursive        = true
            }));
        }

        return items;
    }

    // ── Title matching ────────────────────────────────────────────────────────

    private bool MatchesTitleAndYear(BaseItem a, BaseItem b, double threshold)
    {
        if (a.ProductionYear.HasValue && b.ProductionYear.HasValue
            && a.ProductionYear != b.ProductionYear)
            return false;

        var titleA = NormaliseTitle(a.Name ?? string.Empty);
        var titleB = NormaliseTitle(b.Name ?? string.Empty);

        if (string.IsNullOrEmpty(titleA) || string.IsNullOrEmpty(titleB))
            return false;

        if (titleA == titleB) return true;

        if (a is Episode epA && b is Episode epB)
        {
            if (epA.IndexNumber != epB.IndexNumber || epA.ParentIndexNumber != epB.ParentIndexNumber)
                return false;
        }

        double score = JaroWinkler(titleA, titleB);
        if (score >= threshold)
            _logger.LogDebug("DuplicateFinder: fuzzy match {A} ↔ {B} (score={Score:F3})", a.Name, b.Name, score);

        return score >= threshold;
    }

    private static readonly Regex _subtitleRe   = new(@"\s*[:\-]\s*.*$", RegexOptions.Compiled);
    private static readonly Regex _nonAlphaRe   = new(@"[^a-z0-9\s]",   RegexOptions.Compiled);
    private static readonly Regex _whitespaceRe  = new(@"\s+",            RegexOptions.Compiled);

    private static string NormaliseTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        title = Regex.Replace(title.Trim(), @"^(the|a|an)\s+", string.Empty, RegexOptions.IgnoreCase);
        title = _subtitleRe.Replace(title, string.Empty);
        title = title.ToLowerInvariant();
        title = _nonAlphaRe.Replace(title, string.Empty);
        title = _whitespaceRe.Replace(title, " ").Trim();
        return title;
    }

    // ── Jaro-Winkler ─────────────────────────────────────────────────────────

    private static double JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1.0;

        int len1 = s1.Length, len2 = s2.Length;
        int matchWindow = Math.Max(len1, len2) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;

        bool[] matched1 = new bool[len1];
        bool[] matched2 = new bool[len2];
        int matches = 0, transpositions = 0;

        for (int i = 0; i < len1; i++)
        {
            int start = Math.Max(0, i - matchWindow);
            int end   = Math.Min(i + matchWindow + 1, len2);
            for (int j = start; j < end; j++)
            {
                if (matched2[j] || s1[i] != s2[j]) continue;
                matched1[i] = matched2[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        int k = 0;
        for (int i = 0; i < len1; i++)
        {
            if (!matched1[i]) continue;
            while (!matched2[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        double jaro = (matches / (double)len1
                     + matches / (double)len2
                     + (matches - transpositions / 2.0) / matches) / 3.0;

        int prefix = 0;
        for (int i = 0; i < Math.Min(4, Math.Min(len1, len2)); i++)
        {
            if (s1[i] == s2[i]) prefix++;
            else break;
        }

        return jaro + prefix * 0.1 * (1.0 - jaro);
    }

    // ── Item → DuplicateItem projection ──────────────────────────────────────

    private static DuplicateItem BuildDuplicateItem(BaseItem item, PluginConfiguration config)
    {
        string? resolution = null;
        string? videoCodec = null;
        int bitRateKbps = 0;

        if (config.IncludeMediaInfo && item.RunTimeTicks.HasValue)
        {
            var videoStream = item.GetMediaStreams()
                                  .FirstOrDefault(s => s.Type == MediaStreamType.Video);
            if (videoStream is not null)
            {
                if (videoStream.Width.HasValue && videoStream.Height.HasValue)
                    resolution = $"{videoStream.Width}x{videoStream.Height}";
                videoCodec = videoStream.Codec;
            }
            bitRateKbps = item.TotalBitrate.HasValue ? item.TotalBitrate.Value / 1000 : 0;
        }

        return new DuplicateItem
        {
            Id            = item.Id,
            Name          = item.Name ?? string.Empty,
            Path          = item.Path ?? string.Empty,
            FileSizeBytes  = item.Size ?? -1,
            Resolution    = resolution,
            VideoCodec    = videoCodec,
            BitRateKbps   = bitRateKbps,
            Container     = item.Container,
            TmdbId        = item.GetProviderId(MetadataProvider.Tmdb),
            ImdbId        = item.GetProviderId(MetadataProvider.Imdb),
            MusicBrainzId = item.GetProviderId(MetadataProvider.MusicBrainzTrack),
            QualityScore  = ComputeQualityScore(resolution, bitRateKbps, item.Container, item.Size)
        };
    }

    private static long ComputeQualityScore(
        string? resolution, int bitRateKbps, string? container, long? fileSizeBytes)
    {
        long pixels = 0;
        if (resolution is not null)
        {
            var parts = resolution.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                pixels = (long)w * h;
        }

        int containerBonus = container?.ToLowerInvariant() switch
        {
            "mkv" => 200, "mp4" => 100, "avi" => 50, _ => 0
        };

        long sizeBonus = fileSizeBytes.HasValue ? fileSizeBytes.Value / (1024 * 1024) : 0;
        return pixels * 10 + bitRateKbps + containerBonus + sizeBonus;
    }

    private static string BuildGroupLabel(BaseItem item) => item switch
    {
        Episode ep => $"S{ep.ParentIndexNumber:D2}E{ep.IndexNumber:D2} – {ep.SeriesName}",
        _ when item.ProductionYear.HasValue => $"{item.Name} ({item.ProductionYear})",
        _ => item.Name ?? "Unknown"
    };
}

// ── Union-Find ────────────────────────────────────────────────────────────────

internal sealed class UnionFind<Guid> where Guid : notnull
{
    private readonly Dictionary<Guid, Guid>   _parent = new();
    private readonly Dictionary<Guid, int>    _rank   = new();

    public void MakeSet(Guid x)
    {
        if (!_parent.ContainsKey(x)) { _parent[x] = x; _rank[x] = 0; }
    }

    public Guid Find(Guid x)
    {
        if (!_parent[x].Equals(x)) _parent[x] = Find(_parent[x]);
        return _parent[x];
    }

    public void Union(Guid x, Guid y)
    {
        var rootX = Find(x); var rootY = Find(y);
        if (rootX.Equals(rootY)) return;
        if (_rank[rootX] < _rank[rootY])       _parent[rootX] = rootY;
        else if (_rank[rootX] > _rank[rootY])  _parent[rootY] = rootX;
        else { _parent[rootY] = rootX; _rank[rootX]++; }
    }
}
