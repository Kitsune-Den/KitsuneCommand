# Deploying KitsuneCommand

The `tools/deploy.{sh,ps1}` scripts push a fresh KC build to a remote
7DTD server. They preserve admin-modifiable files, restart the systemd
unit, and verify the service came back up.

For the in-panel "click Update" flow that doesn't need SSH at all, see
kanban [`#139` — KC → In-panel update epic](https://kitsunebi.cloud).
This doc is for today's manual flow + (in time) for CI deploys.

## What gets replaced vs preserved

The deploy script knows two categories of files inside
`Mods/KitsuneCommand/`:

| Category | Examples | Behavior |
|---|---|---|
| **Stock** — ships with every release | `KitsuneCommand.dll`, all dependency DLLs, `wwwroot/*`, `Config/Migrations/*.sql`, `ModInfo.xml`, `x64/sqlite3.dll`, `lib/Chaos.NaCl.dll` | Replaced wholesale every deploy. Orphan files from older builds get cleaned up. |
| **Admin-modifiable** — must survive a deploy | `Config/appsettings.json` (ports, URLs, token expiry), `Plugins/` (admin-dropped plugin DLLs) | Snapshotted before the wipe, restored after. |

State that lives **outside** the mod folder isn't touched at all:
`KitsuneCommand.db` and `KitsuneBackups/` and `KitsuneModpacks/` live
in the game root, not in `Mods/KitsuneCommand/`.

## One-time setup

1. Copy `.deploy.env.example` to `.deploy.env` in the repo root.
2. Fill in `KC_DEPLOY_HOST` at minimum. Defaults handle the rest if
   you're on a standard SteamCMD-as-`ada` setup; override the
   `KC_DEPLOY_*` keys if your paths differ.
3. Make sure `ssh <user>@<host>` works without a password prompt
   (SSH key in `~/.ssh/authorized_keys` on the remote, plus your local
   `ssh-agent` running).

The `.deploy.env` file is gitignored — it carries your host. The
`.deploy.env.example` template stays in git.

## Daily flow

### Build + deploy in one shot

```bash
# Linux / macOS / WSL / Git Bash
tools/deploy.sh --build
```

```powershell
# Windows native PowerShell — uses ssh + scp (built into Win10+)
tools\deploy.ps1 -Build
```

### Deploy without rebuilding (e.g. trying again after a flake)

```bash
tools/deploy.sh
```

```powershell
tools\deploy.ps1
```

### CI / scripts — skip the confirmation prompt

```bash
tools/deploy.sh --build --yes
```

```powershell
tools\deploy.ps1 -Build -Yes
```

## What each phase does

1. **Sanity** — verifies `dist/KitsuneCommand/` exists + has the
   main DLL. If `--build` / `-Build` was passed, runs the build
   script first.
2. **Plan + confirm** — prints what's about to happen, prompts for
   confirmation unless `--yes` / `-Yes` is set.
3. **Snapshot preserved paths** — `Config/appsettings.json` and
   `Plugins/*` get copied to a remote temp dir.
4. **Wipe + copy** — the bash script uses `rsync --delete
   --exclude=...` for the preserve paths in one pass. The PowerShell
   script can't do exclusion with scp, so it removes the mod folder
   entirely (after step 3 saved the bits we care about), scps the
   fresh dist, then restores the snapshot.
5. **Ownership + restart** — `chown -R ada:ada` over the new mod
   folder, then `systemctl restart 7daystodie`.
6. **Health check** — sleep 5s, then `systemctl is-active`. If the
   unit is `active` or `activating` we're done. If it's `failed`
   the script prints the last 20 log lines and exits non-zero.

## When things go sideways

### "The new sidebar item isn't appearing"

99% browser cache. Hard-refresh: `Ctrl+F5` (Chrome/Edge/Firefox),
`Cmd+Option+R` (Safari). Or open the panel in an incognito window.

Vite uses content-hashed filenames for assets, so old chunks coexist
with new ones in `wwwroot/assets/` (that's intentional — `--delete`
removes the orphans during the next sync). The browser-side issue is
the cached `index.html` referencing the old AppLayout hash.

### "Service is `failed` after the restart"

The script will dump the last 20 `journalctl` lines. Common causes:

- **Schema migration error** — a new release added a migration that
  doesn't apply cleanly. Look for `[KitsuneCommand] Applying migration:
  ...` followed by a SQL error.
- **Missing native library** — `DllNotFoundException` for `sqlite3`
  or `libSkiaSharp`. Usually means the `x64/` subfolder didn't make
  it over; re-run the deploy.
- **Port conflict** — KC's WebUrl or WebSocketPort collided with
  another service. Check `Config/appsettings.json` — your snapshotted
  copy should still be there.

### "I need to roll back"

Today: manual. Keep a copy of the previous `KitsuneCommand-vX.Y.Z.zip`
(GitHub Releases keeps them indefinitely). Extract it, re-run the
deploy with `--build` swapped for the previous version's `dist/`.

Roadmap: in-panel update flow (#139) tracks the previous version
in-place at `KitsuneCommand.previous-vX.Y.Z/` for 24h after an update,
with a one-click rollback button.

## Configuration reference

All keys read from `.deploy.env` or process env vars:

| Key | Required | Default | What |
|---|---|---|---|
| `KC_DEPLOY_HOST` | yes | — | SSH host of the panel server |
| `KC_DEPLOY_USER` | no | `root` | SSH user; needs chown + systemctl rights |
| `KC_DEPLOY_MODS_PATH` | no | `/home/ada/7d2d-server/Mods` | remote 7DTD Mods dir |
| `KC_DEPLOY_SERVICE_USER` | no | `ada` | file owner the chown step sets |
| `KC_DEPLOY_SERVICE_NAME` | no | `7daystodie` | systemd unit to restart |

## Future work

- **In-panel update flow** (kanban #139–#144) — admin clicks a button
  in the web panel, no SSH involved. Same preserve-paths logic;
  hand-off through the systemd pre-start hook does the swap so KC
  can update its own DLLs while the 7DTD process is briefly down.
- **GitHub Actions auto-deploy** — same `tools/deploy.sh` invoked from
  a workflow on tag push, with the SSH key in CI secrets. Worth
  doing once the release pipeline (#136) lands so the tag → built
  zip → deploy chain is one step.
