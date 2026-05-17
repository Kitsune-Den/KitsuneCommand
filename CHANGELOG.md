# Changelog

All notable changes to KitsuneCommand are documented here. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to a semver-ish versioning convention documented in
[`docs/RELEASES.md`](docs/RELEASES.md).

For richer release prose (screenshots, migration notes, troubleshooting), see
the matching [GitHub release](https://github.com/AdaInTheLab/KitsuneCommand/releases).
This file is the single source of truth that the (future) release workflow
pulls notes from — it's the minimum, the GitHub release page is the maximum.

## [Unreleased]

### Added

- KC → PackRelay publish path. New **PackRelay** sidebar entry lets server
  owners publish a curated modpack to packrelay.cloud — signed manifest,
  content-addressed file uploads, idempotent re-runs. Five stages: C# crypto
  primitives (canonical-JSON + Ed25519 + SHA-256), HTTP client, publish
  orchestrator, encrypted credentials at rest (AES-256-CBC + HMAC-SHA256),
  controller + Vue tab with live progress. (#129, #130, #131, #132, #133)
- `KitsuneCommand.csproj` now copies `x64/sqlite3.dll` to the build output,
  unblocking 32 previously-failing DB-backed integration tests on the test
  runner.
- `TestAssemblySetUp.cs` preloads `x64/sqlite3.dll` via `LoadLibrary` so
  `[DllImport("sqlite3")]` resolves under net48 testhost — mirrors what
  `ModEntry.cs` does at production runtime.

### Fixed

- `AuthService.ChangePassword` test now asserts against the correct
  `UpdatePassword(id, hash)` shape. The test had drifted from the production
  fix that landed earlier (the old `Update(account)` path silently dropped
  password changes).

### Internal

- Test suite went from 32 failed / 30 passed to 177 passed / 0 failed / 3
  skipped (intentional `[Ignore]` for game-runtime-dependent paths). The
  3 skips cover paths that need a live 7DTD process to exercise.

## [2.6.4] - 2026-05-13

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.6.4)
> · Folds v2.6.3 (bumped on main but never tagged) into this release.

### Added

- **Modpack** feature — admin curates installed mods into a player-facing
  bundle, builds a zip, three-state workflow (draft → published → archived).
  Once published, a top-right download CTA appears on the login page so
  players can grab matching client-side mods without a panel account.
  Anonymous metadata + download endpoints, atomic temp-file zip build.
  (PR #64)
- **Graceful Restart** — daily restarts with player-friendly in-game
  countdown warnings. Configurable warning ladder, IANA-timezone schedule
  (DST-aware), `krestart [minutes]` console command, manual "Restart Now"
  button. Replaces the systemd-timer + shell-script stopgap from v2.6.1.
  (PRs #57, #58)
- **German, French, Spanish locales** — ~250 keys / 30 namespaces in each.
  Brings the supported locale count to 8 total.
  (PRs #59, #61, #62)
- **Favicon refresh** — regenerated all four variants from the brand logo,
  driven by `tools/regen-favicons.py` (Pillow only, single-source-of-truth).
  (PR #60)

### Fixed

- **Backups silent write loss** — `BackupService` had a redundant
  `conn.Open()` on every method (six call sites). The custom
  `System.Data.SQLite` build silently dropped INSERT/UPDATE after a
  double-Open. ZIPs appeared on disk but the audit table stayed empty.
  (PR #56)

### Changed

- README — features list now reflects Vote Rewards (was missed in v2.6.2's
  README pass), Graceful Restart, Trader Zone Toggle, and Modpack.
  (PRs #63, #65)

## [2.6.2] - 2026-04-30

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.6.2)

### Added

- **Vote-for-Rewards integration** — admin lists their server on
  7daystodie-servers.com (and pluggable future providers); when players vote,
  KC dispatches a reward (Points / VIP Gift; CD Key reserved). Two trigger
  paths share an idempotent insert-first grant primitive: 60s background
  sweep + `/vote` chat command, both guarded by a `vote_grants` unique index
  on `(provider, steam_id, vote_date)` so concurrent sweeps + races against
  `/vote` can't double-grant. EOS-based player ID resolution to match V2's
  cross-platform identity. (PRs #46, #47, #48, #49, #50, #51, #53, #54)
- **`/help`, `/commands`, `/?` chat commands** — players list every enabled
  command on the server, grouped by feature (Home, Teleport, Points, Store,
  VIP, Tickets, Blood Moon, Vote Rewards). Only enabled groups appear. (PR #44)
- **Panel screenshots in README** — eight click-to-zoom panels covering the
  main admin surfaces. (PRs #42, #43)
- `docs/troubleshooting.md` — new troubleshooting index covering the V2.x
  EOS migration's "N/A server browser" downstream effect. (PR #45)

## [2.6.1] - 2026-04-26

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.6.1)
> · Patch rollup; especially relevant if running behind Cloudflare Tunnel,
> on Linux, or with the Discord bot enabled.

### Added

- **`kcresetpw` console command** — admin password recovery from the 7D2D
  game console, telnet, or KC's Console view. `kcresetpw <username>
  <newpassword>`, min 8 chars, admin-only. (PR #37)

### Fixed

- **Discord "Server Online" notification** — was racing the bot's gateway
  handshake (~1.3s gap). Two-flag tracking (`_gameStarted` + `_botReady`)
  now fires exactly when both are true, idempotently across reconnects.
  (PR #40)
- **WebSocket Live badge on prod** — WebSocketSharp's strict Host-header
  validation rejected Cloudflared's forwarded `panel.example.com` host. New
  example tunnel config in `docs/cloudflared-tunnel.example.yml`; six other
  prod-only failure modes indexed in `docs/troubleshooting.md`. The
  WebSocket endpoint also moved from `/ws` to `/kctunnel`.
  (PRs #38, #39)
- **Map view on Linux** — SkiaSharp's `[DllImport("dl")]` had no Mono dllmap.
  Ships `SkiaSharp.dll.config` with the proper mapping; build scripts now
  copy `libSkiaSharp.so` to `x64/` (not `linux-x64/`). (PR #34)
- **Settings → Change Password** — now routes through `UpdatePassword(id,
  hash)` instead of the generic `Update(account)` that silently dropped
  `password_hash` changes. (PR #36)

### Changed

- **Login + sidebar polish** — KC logo above the brand mark in the sidebar
  + on the login page; password input full-width; language selector moved
  to the login footer; fox-head favicon. (PRs #33, #35)

## [2.6.0] - 2026-04-23

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.6.0)

### Added

- **Smart mod upload** — Mods Manager handles real-world zip shapes that
  previously broke metadata detection: case-insensitive recursive walk for
  `ModInfo.xml`, Windows-zipped backslash-path normalization, mod-pack
  support (multiple `ModInfo.xml` per zip = multiple installs), Nexus
  suffix stripping, auto-generated minimal `ModInfo.xml` for missing ones.
- **Day/Night Cycle visual widget** in the Config Editor — slider + warm/cool
  split bar + compact day/night minute spinners.
- **Discord restart-required banner** — first-time bot setup needed a
  restart to take effect; now a yellow banner with an inline "Restart now"
  button appears after save.

### Changed

- **Config Editor** — rewritten help text for every field with vanilla
  reference numbers; group reshape (World / Player / Gameplay / Block
  Damage); humanized labels via `formatFieldLabel` fallback.
- **Reverse-proxy URL compat** — login fetch uses a relative path;
  WebSocket connects on same origin unless the page is at `:8890` directly.
  Works through HTTPS proxies (Cloudflare Tunnel, Caddy, nginx).
- **Mod version is dynamic** — `ModInfo.xml` is the single source of truth,
  read at startup into `ModEntry.ModVersion`, exposed via
  `/api/server/info`, consumed by the sidebar badge + Dashboard info row.
- **Dashboard layout** — KitsuneCommand row back on the info card middle
  slot so Local IP + Public IP align on the final row.

## [2.5.0] - 2026-04-22

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.5.0)

### Added

- **Trader Protection Toggle** — admins can temporarily disable trader-area
  block protection to clean up broken blocks via the `ktrader on|off`
  console command. Server-side enforcement via Harmony prefixes on
  `GameManager.ChangeBlocks` + `NetPackageSetBlock.ProcessPackage` so it
  holds against modded clients that bypass client-side checks.
- **KitsuneTraderUnlock** companion client mod — drop-in client mod for V2's
  locked-down client. Skips patching on dedicated servers (batchmode) so
  it's safe to sync to a shared Mods directory.
- **Server Auto-Update** — web UI for `AutoUpdate`, branch, Steam credentials.
  systemd `ExecStartPre` hook (`kitsune-pre-start.sh`) handles steamcmd
  update + log rotation + "sticky" `serverconfig.xml` restore.
- **Restart button on Server Control**.
- **Dashboard reachability indicator** + port-inline IP display.

### Changed

- **Config Editor** — card layout + dark theme.

### Fixed

- **Config Editor saves persist across restarts** — `.bak` was being
  captured BEFORE edits and restored on every start, wiping saves.
- Linux/Mono compat: `System.ComponentModel.DataAnnotations.dll` HintPath
  fix.
- Missing i18n keys for `common.reload` and `common.warning`.

## [2.2.1] - 2026-04-02

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.2.1)
> · **Critical fix — nobody could log in on v2.2.0.**

### Fixed

- **Login authentication** — OWIN OAuth `/token` endpoint doesn't work
  under 7D2D's Unity/Mono runtime, and `BCrypt.Net.BCrypt.Verify()` produces
  incorrect results. Workaround: standalone login endpoint on port **8890**
  bypasses the broken OWIN OAuth middleware; BCrypt hash-and-compare
  workaround re-hashes input with stored salt instead of using broken
  `Verify()`.

### Added

- **Built-in static file server** on port 8890 serves the Vue frontend.
- **API reverse proxy** forwards authenticated `/api/*` requests to port 8888.
- **Emergency password reset** — place a `RESET_PASSWORD.txt` in the
  save-game `KitsuneCommand` folder to recover admin access.

### Changed

- **New port — web panel moves to 8890** (was 8888).

## [2.2.0] - 2026-03-09

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.2.0)
>
> ⚠️ **Login was broken on this release.** See v2.2.1.

### Added

- **Linux support** — same release zip works on both Windows and Linux.
  Auto-detects OS and loads the correct native libraries.
- **Cross-platform native library loading** — `dlopen` on Linux,
  `LoadLibrary` on Windows, with Mono `mono_dllmap_insert` for runtime
  DLL mapping.
- **Built System.Data.SQLite from source** with `SQLITE_STANDARD` define —
  the NuGet binary uses obfuscated P/Invoke names that break Mono DLL
  mapping on Linux.
- **Official `sqlite3.dll`** from sqlite.org for Windows (the NuGet
  `SQLite.Interop.dll` doesn't export standard SQLite C API functions).
- **PlatformHelper** utility for cross-platform OS detection.
- **Build scripts** support `-Platform` parameter (`windows`, `linux`,
  `both`).
- `System.ComponentModel.DataAnnotations.dll` shipped with the mod
  (required by ASP.NET Web API, not included in Unity's Mono).

## [2.1.0] - 2026-03-06

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.1.0)

### Added

- **Player Cards view** — toggle between card grid and table view on the
  Players page. Cards show avatar, level, zombie kills, deaths, health bar,
  position, plus quick actions (view details, edit metadata, PM, give items,
  kick/ban).
- **Player metadata** — custom name colors per player (panel + in-game
  chat), custom tags (VIP, Supporter, Moderator), admin notes, panel-side
  in-game admin level change.
- **Ticket system** — in-game player support tickets, view/respond/manage
  from the panel.
- **Give Items dialog** — give items to players directly from the panel
  with name, count, quality.
- README banner logo.

### Fixed

- **WebSocket events not reaching frontend** — `EventBroadcaster` was
  serializing JSON in PascalCase while the frontend expected camelCase.
- **WebSocket idle disconnects** — disabled WebSocketSharp's `KeepClean`
  auto-close behavior.
- **Dashboard text contrast** — PrimeVue `Card` component was overriding
  text color via CSS variable.
- **Nav active state** — Dashboard was always highlighted due to Vue Router
  prefix-matching on `/`.

## [2.0.0] - 2026-03-04

> [Full notes](https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.0.0)

Clean-room V2 rewrite of [ServerKit](https://github.com/IceCoffee1024/7DaysToDie-ServerKit).
The 2.0 cut, not a continuation of v1.x.

### Added

- **Web Dashboard** — real-time server stats, player count, FPS, memory,
  game day/time.
- **GPS Map** — live Leaflet map with player markers and SkiaSharp tile
  rendering.
- **Web Console** — execute server commands from the browser with log
  streaming.
- **Player Management** — view online/offline players, inventories,
  kick/ban.
- **Economy System** — Points, sign-in rewards, configurable shop, CD keys,
  VIP gifts.
- **Teleportation** — home, city, friend teleport.
- **Self-hoster tools** — Server Control, Config Editor, Mods Manager, Auto
  Backup, Task Scheduler.
- **5 languages** — English, Japanese, Korean, Chinese Simplified, Chinese
  Traditional.

### Tech stack

| Layer | Technology |
|---|---|
| Backend | C# 11 / .NET Framework 4.8 / OWIN / ASP.NET Web API 2 |
| Frontend | Vue 3 / TypeScript 5 / Vite 6 / PrimeVue 4 |
| Database | SQLite / Dapper |
| Auth | OAuth2 with BCrypt password hashing |
| Map | SkiaSharp tile rendering + Leaflet |

### Known issues

- `libSkiaSharp.dll` not included; manual add to `x64/` from SkiaSharp 2.80.4
  NuGet package required for map tile rendering.

---

[Unreleased]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.6.4...HEAD
[2.6.4]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.6.2...v2.6.4
[2.6.2]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.6.1...v2.6.2
[2.6.1]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.6.0...v2.6.1
[2.6.0]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.5.0...v2.6.0
[2.5.0]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.2.1...v2.5.0
[2.2.1]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.2.0...v2.2.1
[2.2.0]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.1.0...v2.2.0
[2.1.0]: https://github.com/AdaInTheLab/KitsuneCommand/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/AdaInTheLab/KitsuneCommand/releases/tag/v2.0.0
