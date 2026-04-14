using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.DuplicateFinder.Detection;
using Jellyfin.Plugin.DuplicateFinder.ScheduledTasks;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DuplicateFinder.Api;

[ApiController]
[Route("DuplicateFinder")]
[Authorize(Policy = "RequiresElevation")]
public class DuplicateFinderController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<DuplicateFinderController> _logger;

    public DuplicateFinderController(
        ILibraryManager libraryManager,
        ILogger<DuplicateFinderController> logger)
    {
        _libraryManager = libraryManager;
        _logger         = logger;
    }

    /// <summary>Returns the duplicate groups from the most recent completed scan.</summary>
    [HttpGet("Results")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DuplicateResultsResponse> GetResults()
    {
        return Ok(new DuplicateResultsResponse
        {
            LastRunAt   = ScanLibraryTask.LastRunAt?.ToString("o"),
            TotalGroups = ScanLibraryTask.LastResults.Count,
            TotalItems  = ScanLibraryTask.LastResults.Sum(g => g.Items.Count),
            Groups      = ScanLibraryTask.LastResults
        });
    }

    /// <summary>
    /// Returns the live progress of a running scan, or null if no scan is in progress.
    /// The config page polls this every second while scanning.
    /// </summary>
    [HttpGet("Progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanProgressResponse> GetProgress()
    {
        return Ok(new ScanProgressResponse
        {
            IsScanning      = ScanLibraryTask.IsScanning,
            PercentComplete = ScanLibraryTask.CurrentProgress?.PercentComplete ?? 0,
            CurrentItem     = ScanLibraryTask.CurrentProgress?.CurrentItem ?? string.Empty,
            ItemsProcessed  = ScanLibraryTask.CurrentProgress?.ItemsProcessed ?? 0,
            ItemsTotal      = ScanLibraryTask.CurrentProgress?.ItemsTotal ?? 0,
            Phase           = ScanLibraryTask.CurrentProgress?.Phase.ToString() ?? string.Empty
        });
    }

    /// <summary>Triggers an immediate scan on a background thread.</summary>
    [HttpPost("Scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult TriggerScan()
    {
        if (ScanLibraryTask.IsScanning)
            return Ok(new { message = "Scan already in progress." });

        _logger.LogInformation("DuplicateFinder: manual scan triggered via API");

        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var detectorLogger = HttpContext.RequestServices
            .GetService(typeof(ILogger<DuplicateDetector>)) as ILogger<DuplicateDetector>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DuplicateDetector>.Instance;

        var detector = new DuplicateDetector(_libraryManager, detectorLogger);

        // Run on a background thread so the HTTP response returns immediately.
        // The config page polls /Progress to follow along.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var results = detector.FindDuplicates(
                    config,
                    System.Threading.CancellationToken.None,
                    p => ScanLibraryTask.UpdateProgress(p));

                ScanLibraryTask.SetResults(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DuplicateFinder: background scan failed");
                ScanLibraryTask.SetResults(System.Array.Empty<DuplicateGroup>());
            }
        });

        return Ok(new { message = "Scan started." });
    }
}

public class DuplicateResultsResponse
{
    [JsonPropertyName("lastRunAt")]
    public string? LastRunAt   { get; init; }

    [JsonPropertyName("totalGroups")]
    public int TotalGroups     { get; init; }

    [JsonPropertyName("totalItems")]
    public int TotalItems      { get; init; }

    [JsonPropertyName("groups")]
    public IReadOnlyList<DuplicateGroup> Groups { get; init; } = [];
}

public class ScanProgressResponse
{
    [JsonPropertyName("isScanning")]
    public bool   IsScanning      { get; init; }

    [JsonPropertyName("percentComplete")]
    public int    PercentComplete  { get; init; }

    [JsonPropertyName("currentItem")]
    public string CurrentItem     { get; init; } = string.Empty;

    [JsonPropertyName("itemsProcessed")]
    public int    ItemsProcessed  { get; init; }

    [JsonPropertyName("itemsTotal")]
    public int    ItemsTotal      { get; init; }

    [JsonPropertyName("phase")]
    public string Phase           { get; init; } = string.Empty;
}
