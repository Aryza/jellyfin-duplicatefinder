using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DuplicateFinder.Detection;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DuplicateFinder.ScheduledTasks;

public class ScanLibraryTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ScanLibraryTask> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── In-memory state (also backed to disk) ─────────────────────────────────
    internal static IReadOnlyList<DuplicateGroup> LastResults { get; private set; }
        = Array.Empty<DuplicateGroup>();

    internal static DateTimeOffset? LastRunAt     { get; private set; }
    internal static ScanProgress?   CurrentProgress { get; private set; }
    internal static bool            IsScanning    { get; private set; }

    internal static void UpdateProgress(ScanProgress p)
    {
        CurrentProgress = p;
        IsScanning      = true;
    }

    internal static void SetResults(IReadOnlyList<DuplicateGroup> results)
    {
        LastResults     = results;
        LastRunAt       = DateTimeOffset.UtcNow;
        CurrentProgress = null;
        IsScanning      = false;
        SaveToDisk(results);
    }

    // ── Disk persistence ──────────────────────────────────────────────────────

    private static void SaveToDisk(IReadOnlyList<DuplicateGroup> results)
    {
        try
        {
            var path = Plugin.ResultsPath;
            if (string.IsNullOrEmpty(path)) return;

            var payload = new PersistedResults
            {
                SavedAt = DateTimeOffset.UtcNow,
                Groups  = results
            };
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal — results are still in memory for this session
        }
    }

    private static IReadOnlyList<DuplicateGroup> TryLoadFromDisk()
    {
        try
        {
            var path = Plugin.ResultsPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<DuplicateGroup>();

            var json    = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<PersistedResults>(json, JsonOpts);
            if (payload?.Groups is null) return Array.Empty<DuplicateGroup>();

            LastRunAt = payload.SavedAt;
            return payload.Groups;
        }
        catch
        {
            return Array.Empty<DuplicateGroup>();
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ScanLibraryTask(ILibraryManager libraryManager, ILogger<ScanLibraryTask> logger)
    {
        _libraryManager = libraryManager;
        _logger         = logger;
        // Load persisted results on first construction (Plugin.ResultsPath is set by now)
        if (LastResults.Count == 0)
            LastResults = TryLoadFromDisk();
    }

    // ── IScheduledTask ────────────────────────────────────────────────────────

    public string Name        => "Scan for Duplicates";
    public string Key         => "DuplicateFinderScan";
    public string Description => "Scans the media library for duplicate movies, TV episodes, and music tracks.";
    public string Category    => "Duplicate Finder";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type           = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek      = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DuplicateFinder: starting library scan");
        IsScanning = true;

        var config         = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var detectorLogger = new LoggerAdapter<DuplicateDetector>(_logger);
        var detector       = new DuplicateDetector(_libraryManager, detectorLogger);

        var results = detector.FindDuplicates(config, cancellationToken, p =>
        {
            CurrentProgress = p;
            progress.Report(p.PercentComplete);
        });

        SetResults(results);

        _logger.LogInformation(
            "DuplicateFinder: scan complete – {Count} duplicate groups found", results.Count);

        return Task.CompletedTask;
    }
}

// ── Persisted payload ─────────────────────────────────────────────────────────

internal sealed class PersistedResults
{
    public DateTimeOffset         SavedAt { get; set; }
    public IReadOnlyList<DuplicateGroup>? Groups  { get; set; }
}

// ── Logger adapter ────────────────────────────────────────────────────────────

internal sealed class LoggerAdapter<T> : ILogger<T>
{
    private readonly ILogger _inner;
    public LoggerAdapter(ILogger inner) => _inner = inner;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}
