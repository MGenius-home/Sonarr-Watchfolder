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

## Phase 2 — Stop the daily log/history spam

**Verified on server (2026-08-23):** `RemoteSyncHistory` = 28 Skipped vs 2 Added rows.

Root causes identified:

- `RemoteSeriesSyncService.cs:70` inserts a "Skipped" `RemoteSyncHistory` row for every
  already-present series on every sync run (table grows unbounded, UI noise).
- `LocalFolderWatcherService.cs:89,108,118,157` re-logs Unmapped/Ignored files on every
  scan (default every 5 min).

- [ ] RemoteSync: stop recording "Skipped" history rows entirely; keep rows only for
      state changes (Added / Failed). Log one summary line: `skipped=N`.
- [ ] Watcher: only log Unmapped/Ignored decisions once per file (log at Debug on
      subsequent scans, Info on first decision or status change).
- [ ] Add history table pruning (e.g. delete rows older than 30 days) during the sync run.
- [ ] Optional: surface counts in the RemoteSync UI header instead of raw row spam.

## Phase 3 — Rework the ingest ("move file") implementation

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

- [ ] Make watch path configurable (`SONARR_WATCH_FOLDER`, default `/watch`)
- [ ] Route all FS access through `IDiskProvider`
- [ ] Strengthen settling: require unchanged size across two scans OR mtime-age threshold,
      whichever is safer; skip files still open by another process where detectable
- [ ] Ignore files < 50 MB (configurable) unless already tracked
- [ ] Prune buffer rows whose paths no longer exist on disk
- [ ] Add failure backoff: after N consecutive failures mark `Failed` and stop retrying;
      log once, count thereafter
- [ ] Configurable import mode (keep `Copy` as the safe default)
- [ ] Batch decisions grouped by parsed series to cut redundant lookups
- [ ] Unit tests: settle logic, extension filter, decision routing (Import/No-Upgrade/
      Ignored/Unmapped), buffer pruning. Run via `dotnet test` on the Core test project.

## Phase 4 — Upstream sync (security first)

Policy: apply bug + **security** fixes. Skip anything that exists to fetch media from
the internet (download clients, indexers, release searching) — this fork never uses them.

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
