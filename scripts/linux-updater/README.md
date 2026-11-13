# Linux Updater

One-time setup for the Linux `ServerUpdateFeature` — mirrors the Windows `launch.bat` pattern (steamcmd update → restore config → rotate logs → start server) but hooks it into systemd.

You only need this if the server runs as a systemd unit. A server started
straight from `startserver.sh` needs no setup for the Restart button: KC
relaunches it itself (it won't run the steamcmd update step, though).

## Install

From any machine with SSH access to the server (or directly on the server):

```bash
cd /path/to/KitsuneCommand/scripts/linux-updater
./install-linux-updater.sh /home/ada/7d2d-server 7daystodie.service
```

Arguments:
1. Server install directory (default `$HOME/7d2d-server`)
2. systemd service name (default `7daystodie.service`)

Prompts for sudo to modify the service unit and write a sudoers entry.

## What it installs

- `kitsune-pre-start.sh` → dropped into the server directory
- `/etc/systemd/system/<service>.d/kitsune-updater.conf` → adds `ExecStartPre`, `Restart=always`, `RestartSec=5`
- `/etc/sudoers.d/kitsune-7d2d-<user>` → allows the service user to restart/stop/start the service without a password (needed so KC's "Update Now" button can trigger a restart)

If `serverconfig.xml.bak` doesn't exist, it's seeded from the current `serverconfig.xml` so your existing settings become the sticky baseline.

## How it works at runtime

Every systemd start, before the server boots:

1. `kitsune-pre-start.sh` sources `kitsune-update.conf` (written by KC's `ServerUpdateFeature`)
2. If `AutoUpdate=true`: run `steamcmd +app_update <SteamAppId> [-beta <Branch>] validate`
3. Restore `serverconfig.xml` from `serverconfig.xml.bak` (because validate just clobbered it)
4. Trim `output_log__*.txt` to the newest `LogRetention` (default 20)
5. Hand off to `ExecStart` which runs `startserver.sh`

KC doesn't run `steamcmd` itself — that can't happen inside the running game process. KC just writes the config and triggers a service restart; the pre-start hook does the work.

## First-time steamcmd login

`steamcmd` with `+login anonymous` works for the dedicated-server app (294420) on Windows, but on Linux you'll likely be using app 251570 (the main game, the only flavor with Linux depots). 251570 requires a Steam login. Do this once interactively:

```bash
sudo -u ada steamcmd +login <your-steam-username> +quit
```

Enter your password + Steam Guard code. Credentials get cached. After that, the pre-start script runs `+login anonymous` — but steamcmd on subsequent runs will use the cached login if available. If anonymous doesn't work for your app ID, edit `kitsune-pre-start.sh` to use your username instead.

## Uninstall

```bash
sudo rm /etc/systemd/system/<service>.d/kitsune-updater.conf
sudo rm /etc/sudoers.d/kitsune-7d2d-<user>
sudo systemctl daemon-reload
rm /home/ada/7d2d-server/kitsune-pre-start.sh
rm /home/ada/7d2d-server/kitsune-update.conf  # optional - KC recreates on next start
```

`serverconfig.xml.bak` is your edit; leave it or delete per taste.
