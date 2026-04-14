# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```bash
dotnet build -c Release
```

Output: `bin/Release/net9.0/Jellyfin.Plugin.DuplicateFinder.dll`

There are no automated tests in this project. Verification requires deploying the `.dll` to a running Jellyfin instance.

## Architecture

This is a Jellyfin 10.11.x plugin targeting `net9.0`. The plugin is loaded by Jellyfin's plugin host, which provides dependency injection for `ILibraryManager`, `ILogger<T>`, etc.

**Data flow for a scan:**

1. `ScanLibraryTask` (or `DuplicateFinderController.TriggerScan`) calls `DuplicateDetector.FindDuplicates()`
2. `DuplicateDetector` fetches items via `ILibraryManager`, then runs two passes:
   - Provider ID buckets (TMDb, IMDB, MusicBrainz, series S×E key) — zero false positives
   - Fuzzy title+year buckets (Jaro-Winkler ≥ threshold, coarse first-word+year pre-grouping)
3. Both passes feed into a `UnionFind<Guid>` for transitive grouping (A≡B + B≡C → one group)
4. Results are stored in static fields on `ScanLibraryTask` and persisted to `duplicatefinder_results.json` in Jellyfin's data directory
5. `DuplicateFinderController` exposes the results and live scan progress via REST endpoints

**Key design constraints:**
- Items are fetched in separate queries per type (Movie, Episode, Audio) to avoid Jellyfin's EF `MultipleCollectionInclude` warning
- `ScanLibraryTask.LastResults` / `IsScanning` / `CurrentProgress` are static — shared state between the scheduled task and the API controller
- `Plugin.cs` registers a single web page, `DuplicateFinder` (served from `Configuration/configPage.html`), with `EnableInMainMenu = true` so it acts as both the Dashboard → Plugins settings target and the sidebar entry. The page renders two tabs (Settings & Scan / Report) in client-side JS. Do **not** set `DisplayName`, `MenuSection`, or `MenuIcon` on `PluginPageInfo` — those properties cause a Jellyfin load crash despite compiling cleanly
- Results are written to disk as camelCase JSON; the `PersistedResults` wrapper includes a `SavedAt` timestamp loaded back on startup

**API endpoints** (all require `RequiresElevation` policy):
- `GET /DuplicateFinder/Results` — last completed scan results
- `GET /DuplicateFinder/Progress` — live scan progress (polled by the report page)
- `POST /DuplicateFinder/Scan` — trigger an immediate background scan

**Quality scoring formula** (used to rank items within a group and mark the ★ keep candidate):
```
score = (width × height) × 10 + bitrate_kbps + container_bonus + size_mb
container_bonus: mkv=200, mp4=100, avi=50
```

**Title normalisation pipeline** (applied before Jaro-Winkler):
1. Strip leading articles (`the`, `a`, `an`)
2. Strip subtitle after `:` or ` - `
3. Lowercase, remove non-alphanumeric chars, collapse whitespace
