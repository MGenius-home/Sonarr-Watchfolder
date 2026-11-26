# Roadmap — Sonarr-Watchfolder (local-only copier)

Basis: `PROJECT_DESIGN_SPEC.md` — automated ingest from `/watch` into the library,
plus remote series sync from an upstream Sonarr. Forked from Sonarr; download-from-internet
features are out of scope for fixes.

---

## Phase 1 — Ship prebuilt images (pull instead of rebuild) ✅ DONE 2026-08-23

Goal: server never builds. It pulls from GHCR.

- [x] Add `.github/workflows/docker-image.yml`:
  - Triggers: push to `feature/local-ingest`, manual `workflow_dispatch`
  - Build the multi-stage `Dockerfile` with `docker/build-push-action` + GHA layer cache
  - Push to `ghcr.io/mgenius-home/sonarr-watchfolder` with tags `latest`, `sha-<short>`, and date tag
  - Permissions: `packages: write`; login via `GITHUB_TOKEN`
- [x] Clean up `Dockerfile`: remove duplicate `apt-get update` (line 18)
- [x] Rewrite `deploy.sh` on the server flow: `git pull && docker compose pull && docker compose up -d --remove-orphans`
  (no more `docker build --no-cache`)
- [x] Update `docker-compose.yml` to reference the GHCR image instead of `build: .`
  (kept as a generic public example; real stack config lives on the server only)
- [x] First run: image published, anonymous pull verified, server switched over,
      container healthy and UI responding

## Phase 2 — Stop the daily log/history spam ✅ DONE 2026-08-23

**Verified on server (2026-08-23):** `RemoteSyncHistory` = 28 Skipped vs 2 Added rows.
**Verified after fix:** full scan of 340 files produced 3 log lines; existing Skipped
rows purged; history now records only state changes and prunes rows older than 30 days.

Root causes identified:

- `RemoteSeriesSyncService.cs:70` inserts a "Skipped" `RemoteSyncHistory` row for every
  already-present series on every sync run (table grows unbounded, UI noise).
- `LocalFolderWatcherService.cs:89,108,118,157` re-logs Unmapped/Ignored files on every
  scan (default every 5 min).

- [x] RemoteSync: stop recording "Skipped" history rows entirely; keep rows only for
      state changes (Added / Failed). Log one summary line: `skipped=N`.
- [x] Watcher: only log Unmapped/Ignored decisions once per file (log at Debug on
      subsequent scans, Info on first decision or status change).
- [x] Add history table pruning (e.g. delete rows older than 30 days) during the sync run.
- [x] Surface counts (`Added` / `Failed`) and `Sync Now` trigger in the RemoteSync UI.

## Phase 3 — Rework the ingest ("move file") implementation ✅ DONE 2026-08-23

**Status: implemented and verified live.** Migration 237 applied; first scan pruned 290
stale rows; ambiguous titles (Shogun 1980 vs Shōgun 2024) now route to Unmapped with a
single Info line instead of per-scan error traces.
New config: `SONARR_WATCH_FOLDER`, `SONARR_IMPORT_MODE`, `SONARR_WATCH_MIN_SIZE_MB`,
`SONARR_WATCH_SETTLE_SECONDS`, `SONARR_WATCH_FAILURE_THRESHOLD`. Unit tests:
`WatchFolderRulesFixture` (27 cases).

Current known problems in `LocalFolderWatcherService.cs`:

| # | Problem | Where |
|---|---------|-------|
| 1 | Watch path hardcoded to `/watch` | :47 |
| 2 | `new FileInfo(path)` bypasses `IDiskProvider` (breaks abstraction, mono edge cases) | :77 |
| 3 | Settle check = LastWriteTime only; a file that stops growing but keeps mtime old can import mid-copy; no size-stability double-check | :79 |
| 4 | No minimum-size sanity filter (samples/tiny clips become junk entries) | :56-59 |
| 5 | Buffer table never pruned when source files are deleted → DB grows forever, stale Pending entries | repo |
| 6 | Errors leave entry Pending → full retry + error log every scan | :162 |
| 7 | One decision request per file (slow for large folders); could batch per series | :124 |
| 8 | Copy mode hardcoded; design says non-destructive default, but mode should be config (`SONARR_IMPORT_MODE=Copy|Move|Hardlink`) with Copy default | :149 |

**Verified on server (2026-08-23):** `LocalWatchBuffer` = 408 Imported, 112 Unmapped,
104 Ignored, 6 Pending (630 rows, never pruned).

Tasks:

- [x] Make watch path configurable (`SONARR_WATCH_FOLDER`, default `/watch`)
- [x] Route FS access through `IDiskProvider` (mtime via `FileGetLastWrite`, size via `GetFileSize`, lock check via `IsFileLocked`)
- [x] Strengthen settling: unchanged size across scans + mtime-age threshold + file-lock detection
- [x] Ignore files below minimum size (default 50 MB, configurable) before they enter the buffer table
- [x] Prune buffer rows whose paths no longer exist on disk
- [x] Failure backoff: escalating log levels, hard stop after N failures (`Failed` status)
- [x] Configurable import mode (keep `Copy` as the safe default)
- [x] Batch decisions grouped by series (one decision request per series per scan)
- [x] Unit tests: extension filter, settling, growth, min-size, import-mode parsing

## Phase 4 — Upstream sync ✅ COMPLETE 2026-08-23 (41 upstream commits applied across A–D)

Policy: apply bug + **security** fixes. Skip anything that exists to fetch media from
the internet (download clients, indexers, release searching) — this fork never uses them.

**Merge-base:** bf5d48c76 (= upstream Sonarr v5-develop fork point). Upstream delta:
195 commits (bf5d48c76..Sonarr/Sonarr@v5-develop, audited 2026-08-23).
Remote: `upstream` → https://github.com/Sonarr/Sonarr.git

### Triage result (2026-08-23)

**Batch A — Security / runtime deps (apply first)**
- [x] `30661f86b` .NET 10.0.11 runtime patches (supersedes 10.0.8/9/10 bumps)
- [x] `a8d50c164` sqlite3 3.53.4 (folded into .NET bump pick)
- [x] `fe09a6889` FFprobe 9.0.1 (supersedes 9.0 / 8.1.x)
- [x] npm audit bumps applied selectively: qs, lodash, postcss, babel, core-js, fontawesome, @types/*; KEPT react-router 5 + babel-loader 9 + react-redux stack (upstream's file assumed the Redux-removal rewrite we skip); yarn.lock regenerated in clean container
- [x] Deployed + verified: UI 302, DB v237, zero startup errors

**Batch B — Parsing / MediaInfo ✅ DONE 2026-08-23** (12 commits, zero conflicts; Dapper aligned to 2.1.79 in test project; 1955 parser tests green)
- [x] `ae5b03bcf` date parsed before 4-digit absolute ep number
- [x] `974d5377c` anime season pack parsing
- [x] `dfc37be76` quality parsing from some WEB releases
- [x] `a533a1a46`, `5ef066352` release-group false positives (N-Z-B, Celdra)
- [x] `547cd5b48`, `07af80c50` H.266/VVC support
- [x] `61a73d04f` DTS-HD MA + DTS:X → DTS-X
- [x] `5821e40d4`, `208838d0f`, `ad661cc76`, `edba5ce84` media info fixes

**Batch C — Library/import fixes ✅ DONE 2026-08-23** (10 commits, one conflict resolved in EpisodeRepository; full suite: 5121 tests green)
- [x] `143a007ce` ignore invalid languages in Manual Import (feeds Unmapped workflow)
- [x] `64105f98a` multiple extra files with same extension
- [x] `95c42a92b` slow monitoring changes on large shows
- [x] `890348abe` Manage Episodes media file listing
- [x] `a1abe179a` custom score vs renamed filename pre-import
- [x] `fde8bb0ec` + `bcab4b0da` Jellyfin 12+ notifications (host runs Jellyfin-family apps)
- [x] `8ed9a3c53` stop error-logging missing translation files (log-noise)
- [x] `e95b4d8e5` cleanup post-backup temp files
- [x] `fdb9e3f9f` dispose logging targets

**Batch D — Low-risk hardening ✅ DONE 2026-08-23**
- [x] `cca615b8e` non-ASCII HTTP Basic Auth credentials
- [x] `aea7ea743` send full certificate chain
- [x] `9cba4a29a` miniprofiler off when disabled
- [x] `da2284d7e` cache series path free-space spec
- [x] `036aeadd3` lazy JSON deserialization
- [x] `9c69d3ec2` Happy Eyeballs DNS shortcut

**Skipped on purpose:** everything touching download clients, blocklist, RSS/interactive
search, grab/failed-history, import lists (Trakt/Simkl), delay profiles, notification
features (Ntfy/Pushover), stats page, calendar picker, custom filters — plus the giant
frontend rewrites (Redux removal, react-query conversions, React Router v7) which are
high-conflict/no-value for this fork. Weblate translation commits skipped (churn).

Risks noted: skipping frontend rewrites widens drift from upstream; acceptable for a
personal fork, revisit if ever contributing back.

- [ ] Determine merge-base against upstream (`Servarr/Sonarr`, branch matching our base)
      and record it in this file
- [ ] Generate upstream changelog since base; classify commits: security / bug /
      feature / download-stack (skip)
- [ ] Apply security fixes first (auth, API controllers, XML/HTTP parsing, Datastore),
      each as its own commit with upstream SHA referenced
- [ ] Apply relevant core bug fixes (parsing, importing, UI crashes)
- [x] Repeat quarterly or before any server-facing exposure change

### Phase 4b — Second upstream sync (`upstream/develop` @ `03f822c782`) ✅ COMPLETE 2026-11-25

- **Baseline**: `upstream/develop` at `03f822c782` (385 commits ahead of `upstream/v5-develop`).
- **Batch E — Security & Runtime Dependencies (19 commits + Swashbuckle fix)**:
  - `59a1175b6f` .NET 10.0.15 (`global.json` `10.0.204` + `rollForward: latestFeature`, keeping NUnit 3.14.0 & Ical.Net 4.3.1), `bdeefe8353` FFMpegCore 6.0.0, `03f822c782` FFprobe 9.1, `56d29e02ae` Dapper/MailKit/Polly, `e5617ddaa3` Dapper/Npgsql, `bd3d0229b8` Swashbuckle.AspNetCore 10.3.1 (fixes `Microsoft.OpenApi` CVE), `b59552cfea` + `75788feec8` ImageSharp decoder restriction & advisory suppression, `93bed3223d` remove `System.Drawing.Common`, `c340e50b7f` constant-time API key & password hash comparison, `b0bc052701` path traversal validation, `9efa5a0698` mask passwords/secrets from settings API, `bc7c87def0` disable auth endpoint response cache, `91aeb5260c` + `8e25f16315` + `c018578e4b` + `2247ad959e` log cleansing, `f089df054f` zip extraction hardening, `a83ffea245` OWASP PBKDF2 iterations, `0bdf2087a2` login lockout after failed attempts.
- **Batch F — Parsing & MediaInfo (8 commits)**:
  - `b84a621e99`, `6421bb6218`, `808d483613`, `91c4ae17c0`, `da7fa1f8d7`, `4cfd439cbd`, `b10180c37e`, `d3eee35422`.
- **Batch G — Import, Manual Import & Library (11 commits)**:
  - `7c4c81a418`, `615764804e`, `a2c90d021f`, `8afe4f9c62`, `ae29755cfa`, `69fd6f1344`, `8c50e4469e`, `f382d6e8f9`, `f1513a82ec`, `da990630a1`, `69ff21107b`.
- **Batch H — Core Stability, Caching & Backup (7 commits)**:
  - `460022a72e`, `6c1dba24a2`, `3abdde77e2`, `4ebfc48477`, `c0023b4c22`, `dbb5c33f14`, `1a8d5a2b1b`.
- **Skipped from Batches E–H (with reasons)**:
  - `162dcb5834` (*Reject releases and imports that match multiple series*): skipped due to heavy conflicts across 7 files (deleted test fixtures `MultiSeasonImportFixture`/`FindByTitleInexactFixture` and refactored `SeriesRepository`/`TrackedDownloadService`/`ParsingService` from skipped intermediate commits). `LocalFolderWatcherService` continues catching `MultipleSeriesFoundException`.
  - `70829703c9` (*Don't cache media covers that aren't downloaded*): skipped because it depends on the `?h=` media cover URL hashing rewrite not present in this fork (`MediaCoverService` here uses `?lastWrite=` and already checks `FileExists`).

## Phase 6 — Watchfolder Reliability, UX & Infra Upgrades ✅ DONE 2026-11-25

- [x] Fix `RemoteSeriesSyncService` history pruning cutoff (`-HistoryRetentionDays`)
- [x] Fix `LocalFolderWatcherService`: inspect `ImportResult` list, use `ParsingService.Map` (with filename fallback), check `EpisodeFileId > 0` before `!decision.Approved`
- [x] Migration numbering rule: custom migrations use reserved `900001+` range (`900001_add_local_watch_buffer_reason`) to avoid colliding with upstream migrations `236–241+`
- [x] Auto-clear manually imported files via `IHandle<EpisodeImportedEvent>`
- [x] Scan-start `_repository.All()` dictionary cache (OS-appropriate comparer, single DB update per file), expanded `VideoExtensions`, subfolder `folderInfo` grouping
- [x] `RemoteSeriesSyncService` (`SeriesType`, `SeasonFolder`, `QualityProfileId` resolution/fallback, `SONARR_SYNC_LOOKBACK_DAYS`, `X-Api-Key` header)
- [x] V5 API (`/retry`, `/ignore`, `/scan`, `/sync`, `?status=`) and React UI (`UnmappedTable`/`Layout`, `RemoteSyncTable`/`Layout`)
- [x] `Dockerfile` (`gosu` PUID/PGID entrypoint touching `/config` only, `HEALTHCHECK`), `docker-compose.yml` env docs, `.github/workflows/docker-image.yml` CI test job
- [x] Unit test suites: `LocalFolderWatcherServiceFixture`, `RemoteSeriesSyncServiceFixture`, `RemoteSonarrClientFixture`, expanded `WatchFolderRulesFixture`

## Phase 5 — Testing strategy (server access)

How development/testing works from this Windows machine:

1. **Unit tests (no server needed)**: `dotnet test` on Core test projects locally.
   All Phase 3 logic must be covered here.
2. **Integration testing options** (pick what's available):
   - Preferred: SSH access to the server from here → I can run
     `docker compose logs`, `sqlite3 /config/sonarr.db ...`, restarts directly
     (needs: SSH enabled + key auth, or Tailscale)
   - Fallback: you run commands I provide, paste output back
3. **Staging**: point a second compose service (different port + fresh `/config-test`)
   at a new `sha-<tag>` image before promoting to the live container.

- [x] Decide SSH vs manual-paste workflow → **SSH, working as of 2026-08-23**
- [x] Document chosen workflow (connection details kept in a local, non-tracked ops note):
  - Key-auth SSH to the server; `docker` usable by the deploy user without sudo
  - Stack: container `sonarrwatch` (image from GHCR), upstream Sonarr is a separate container on the same compose network
  - Mounts follow the design spec: `/config`, `/tv`, `/watch`
  - NOTE: an auto-updater (Watchtower) runs on the host — decide whether it may
    auto-pull our `latest` or must be excluded

---

## Done

- [x] Reconcile local copy with `MGenius-home/Sonarr-Watchfolder` remote (`feature/local-ingest`)
- [x] Preserve early prototype on `prototype-backup` branch
