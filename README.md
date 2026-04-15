# Jellyfin.Plugin.DuplicateFinder

Detects duplicate movies, TV episodes, and music tracks in your Jellyfin 10.11.x library.

## Features

- **Provider ID matching** — items sharing a TMDb, IMDB, or MusicBrainz ID are flagged immediately with zero false positives.
- **Fuzzy title + year matching** — catches `Avatar (2009).mp4` vs `Avatar (2009).mkv` even when provider IDs are absent or differ. Uses Jaro-Winkler similarity with subtitle stripping (`Avatar: Extended Cut` → `avatar`).
- **Media info in results** — resolution, video codec, bitrate, file size, and container are shown per-file so you can decide which copy to keep. The ★ marker indicates the highest-quality candidate.
- **Union-Find grouping** — if A≡B and B≡C, all three end up in the same group rather than two separate pairs.
- **Scheduled task** — runs weekly (configurable). Manual "Scan now" button on the config page.
- **Report-only** — no files are ever touched automatically.

## Installation

### Via plugin repository (recommended)

1. In Jellyfin, open **Dashboard → Plugins → Repositories**.
2. Click the **+** button and add:
   - **Repository Name:** `Aryza`
   - **Repository URL:**
     ```
     https://raw.githubusercontent.com/Aryza/jellyfin-duplicatefinder/main/manifest.json
     ```
3. Save, then go to **Dashboard → Plugins → Catalog**. You should now see **DuplicateFinder** under the **General** category.
4. Click **Install**, then restart Jellyfin.
5. Open **Dashboard → Plugins → DuplicateFinder** (or the sidebar entry) to configure and run.

### Manual install

1. Build the DLL:
   ```bash
   dotnet build -c Release
   ```
   Output: `bin/Release/net9.0/Jellyfin.Plugin.DuplicateFinder.dll`
2. Copy it into your Jellyfin plugins directory:
   - Linux default: `~/.local/share/jellyfin/plugins/DuplicateFinder/`
   - Docker: map a volume and drop it in there
3. Restart Jellyfin.
4. Go to **Dashboard → Plugins → DuplicateFinder** to configure and run.

## Plugin repository manifest

This repo doubles as a Jellyfin plugin repository. The files are:

| File | Purpose |
|---|---|
| [`manifest.json`](./manifest.json) | Lists all available versions with their download URLs and MD5 checksums. Jellyfin polls this file when checking for updates. |
| GitHub [Releases](https://github.com/Aryza/jellyfin-duplicatefinder/releases) | Each version's zipped DLL + `meta.json` is attached as a release asset. The `sourceUrl` in the manifest points here. |

### Releasing a new version

1. Bump `<Version>` in `Jellyfin.Plugin.DuplicateFinder.csproj`.
2. `dotnet build -c Release`
3. Stage the release artifact:
   ```bash
   mkdir -p build/release
   cp bin/Release/net9.0/Jellyfin.Plugin.DuplicateFinder.dll build/release/
   # Update build/release/meta.json: version + timestamp
   (cd build/release && zip -j duplicatefinder_<version>.zip \
        Jellyfin.Plugin.DuplicateFinder.dll meta.json)
   md5 -q build/release/duplicatefinder_<version>.zip
   ```
4. Create the GitHub release and attach the zip:
   ```bash
   gh release create v<version> build/release/duplicatefinder_<version>.zip \
       --title "v<version>" --notes "..."
   ```
5. **Prepend** a new entry to the `versions` array in `manifest.json` (newest first) with the new `sourceUrl`, `checksum`, and `timestamp`.
6. `git commit -am "Release v<version>" && git push`

Jellyfin installations already subscribed to this repo will pick up the update on their next repository refresh.

## Building from source

```bash
dotnet build -c Release
```

Output: `bin/Release/net9.0/Jellyfin.Plugin.DuplicateFinder.dll`

## Architecture

```
Plugin.cs                       IPlugin entry point + IHasWebPages
Configuration/
  PluginConfiguration.cs        User settings (threshold, scope, toggles)
  configPage.html               Embedded HTML served by Jellyfin dashboard
Detection/
  DuplicateDetector.cs          Core engine:
                                  · FetchItems()        — queries ILibraryManager
                                  · SharesProviderId()  — TMDb / IMDB / MusicBrainz
                                  · MatchesTitleAndYear()  — Jaro-Winkler + normalisation
                                  · UnionFind<T>        — transitive grouping
  DuplicateGroup.cs             Result models (DuplicateItem, DuplicateGroup, MatchReason)
ScheduledTasks/
  ScanLibraryTask.cs            IScheduledTask — weekly scan, stores results in-memory
Api/
  DuplicateFinderController.cs  GET /DuplicateFinder/Results
                                POST /DuplicateFinder/Scan
```

## Matching logic

Priority order within each pair of items:

1. **Provider ID** — any shared TMDb / IMDB / MusicBrainz ID → `MatchReason.ProviderId`
2. **Title + year** — Jaro-Winkler ≥ threshold (default 0.90) on normalised title, exact year — `MatchReason.TitleAndYear`
3. Both match → `MatchReason.Both`

Title normalisation strips leading articles (`the`, `a`, `an`), subtitles after `:` or ` - `, punctuation, and lowercases everything. This means `Avatar: Special Edition (2009)` and `Avatar (2009)` normalise to `avatar` and match at score 1.0.

## Quality scoring

Items within a group are sorted by a composite score:

```
score = (width × height) × 10 + bitrate_kbps + container_bonus + size_mb_bonus
```

Container bonus: mkv=200, mp4=100, avi=50. The ★ item in the UI is the suggested keep candidate.

## Tuning

| Scenario | Recommendation |
|----------|----------------|
| Too many false positives | Raise `FuzzyMatchThreshold` to 0.95 |
| Missing obvious duplicates with no provider IDs | Lower threshold to 0.85 |
| Theatrical vs extended cuts shouldn't match | Raise to 0.98 or disable title matching |
| Large library is slow | Disable music scanning if not needed |
