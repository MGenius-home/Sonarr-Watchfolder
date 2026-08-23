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
- [ ] Optional: surface counts in the RemoteSync UI header instead of raw row spam.

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

## Phase 4 — Upstream sync (security first)

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

**Batch D — Low-risk hardening (optional)**
- [ ] `cca615b8e` non-ASCII HTTP Basic Auth credentials
- [ ] `aea7ea743` send full certificate chain
- [ ] `9cba4a29a` miniprofiler off when disabled
- [ ] `da2284d7e` cache series path free-space spec
- [ ] `036aeadd3` lazy JSON deserialization
- [ ] `9c69d3ec2` Happy Eyeballs DNS shortcut

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
- [ ] Repeat quarterly or before any server-facing exposure change

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
