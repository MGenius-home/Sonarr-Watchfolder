# Project Design Spec: Local Ingest & Monitoring Service (v4)

This document outlines the architecture and implementation details for the custom Local Ingest & Monitoring Service integrated into Sonarr v4.

## Core Features
1. **Automated Folder Monitoring**: Periodically scans a designated `/watch` folder for new media files.
2. **File Settling Check**: Ensures files are not processed until they have been "still" for at least 60 seconds (prevents importing partial downloads).
3. **Smart Ingest Decision Engine**:
    - **Import**: Automatically copies files into the library if they match a "Wanted" episode.
    - **No Upgrade**: Prevents overwriting existing library files.
    - **Unmapped Queue**: Routes files that cannot be automatically identified to a manual review UI.
4. **Non-Destructive**: Uses `Copy` mode by default to ensure the source `/watch` folder remains untouched until the user manually cleans it.

## Technical Architecture

### Backend Components (C# / .NET)
- **Service**: `LocalFolderWatcherService.cs` - The main logic engine for scanning and decision making.
- **Task**: `LocalWatchScanCommand.cs` - Integrated into Sonarr's internal task scheduler.
- **Repository**: `LocalWatchBufferRepository.cs` - Manages the state of tracked files in the database.
- **Database**: `LocalWatchBuffer` table added via FluentMigrator.
- **Configuration**: `SONARR_WATCH_INTERVAL` environment variable (default: 5 minutes).

### Frontend Components (React / TypeScript)
- **View**: `UnmappedTable.tsx` - Displays files that require manual intervention.
- **Navigation**: "Unmapped" link added to the Activity sidebar.

### Infrastructure (Docker)
- **Multi-stage Build**: Optimized Dockerfile with `.dockerignore` for fast iteration.
- **Tooling**: `sqlite3` and `mediainfo` included in the runtime image for debugging.
- **Volumes**: `/config`, `/tv`, and `/watch` mounts required.

---

## Today's Updates (May 10, 2026)

### 1. Performance & Stability Improvements
- **Video Filtering**: Updated the scanner to only process video extensions (`.mkv`, `.mp4`, `.avi`, `.ts`, `.m4v`, `.mov`, `.m2ts`, `.mpg`, `.mpeg`, `.wmv`, `.webm`). This prevents the engine from stalling on metadata files or shortcuts.
- **Scan Caching & Subfolder Context**: Loads `LocalWatchBuffer` once per scan (OS-appropriate path comparison), groups pending files by `(Series.Id, ParentFolder)`, and passes parsed subfolder metadata (`folderInfo`) for season-pack folders.
- **IDE Stability**: Implemented `.cursorignore` to prevent the IDE from crashing when indexing the large media folders (`tv/`, `watch/`).
- **Build Speed**: Added `.dockerignore` to exclude build artifacts and media folders, reducing Docker context transfer from 3GB to ~20MB.

### 2. Configuration & Tooling
- **Configurable Scan Interval**: Added support for `SONARR_WATCH_INTERVAL`. If not set in the compose file, the system defaults to 5 minutes.
- **DB Inspection**: Installed `sqlite3` in the production image to allow direct inspection of the `LocalWatchBuffer` table on the server.

### 3. Bug Fixes
- **ORM Mapping**: Resolved `KeyNotFoundException` by correctly registering the `LocalWatchBuffer` model in `TableMapping.cs`.
- **Runtime Dependencies**: Fixed `Sonarr.Mono.dll` missing error by adding unconditional project references.

### Configuration (environment variables)

| Variable | Default | Purpose |
|---|---|---|
| `SONARR_WATCH_FOLDER` | `/watch` | Folder scanned for new video files |
| `SONARR_WATCH_INTERVAL` | `5` | Minutes between scans |
| `SONARR_IMPORT_MODE` | `Copy` | `Copy` or `Move` into the library (`Move` removes the source file) |
| `SONARR_WATCH_ALLOW_UPGRADES` | `false` | When `true`, re-evaluates changed files in `/watch` and upgrades existing `/tv` episodes if FFprobe MediaInfo shows higher resolution (no audio regression) or more audio channels at the same resolution |
| `SONARR_WATCH_MIN_SIZE_MB` | `50` | Files below this size are never tracked |
| `SONARR_WATCH_SETTLE_SECONDS` | `60` | Minimum age since last write before a file is processed |
| `SONARR_WATCH_FAILURE_THRESHOLD` | `5` | Consecutive failures before a file is marked `Failed` and skipped |
| `SONARR_SOURCE_URL` / `SONARR_SOURCE_API_KEY` | – | Upstream Sonarr to pull newly-added series from (sent via `X-Api-Key` header) |
| `SONARR_SYNC_INTERVAL_MINUTES` | `360` | Remote series sync interval |
| `SONARR_SYNC_LOOKBACK_DAYS` | `7` | Lookback window in days for remote sync (`0` syncs all series) |
| `SONARR_WATCH_ROOT_FOLDER` | `/tv` | Root folder used when adding synced series |
| `SONARR_WATCH_QUALITY_PROFILE_ID` | – | Optional local quality profile ID override for synced series (falls back to remote profile if valid locally, else first local profile) |

### Migrations & API
- **Custom Migration Range**: `900001+` is reserved for custom watchfolder migrations (e.g. `900001_add_local_watch_buffer_reason` adding `Reason` to `LocalWatchBuffer`) to avoid colliding with upstream Sonarr migrations (`236+`).
- **Auto-Clear on Import**: `LocalFolderWatcherService` handles `EpisodeImportedEvent` to mark manually imported watchbuffer entries `Imported`.
- **V5 Endpoints**: `GET /api/v5/localwatchbuffer` (`?status=unmapped|failed|all|...`), `POST /api/v5/localwatchbuffer/{id}/retry`, `POST /api/v5/localwatchbuffer/{id}/ignore`, `POST /api/v5/localwatchbuffer/scan`, and `POST /api/v5/remotesync/sync`.
- **Container Entrypoint**: `docker/watchfolder-entrypoint.sh` applies `PUID`/`PGID` via `gosu` and `chown`s `/config` only (never `/tv` or `/watch`).

### Ingest behaviour
- **Settling**: a file is only imported when its size is unchanged since the previous
  scan AND it has not been written to for the settle period. Locked files are skipped.
- **Failure backoff**: repeated processing failures escalate from Warn to Debug and,
  after the threshold, mark the entry `Failed` so it stops retrying.
- **Pruning**: buffer rows whose source file has disappeared are deleted each scan.
- **Non-destructive default**: Copy mode keeps `/watch` untouched.

---

## Operational Commands (On Server)

**Rebuild Image:**
```bash
sudo docker build --no-cache -t sonarr-watch .
```

**Check DB Status:**
```bash
sudo docker exec sonarr sqlite3 /config/sonarr.db "SELECT * FROM LocalWatchBuffer;"
```

**Check Logs:**
```bash
sudo docker compose logs -f sonarr | grep "LocalFolderWatcherService"
```
