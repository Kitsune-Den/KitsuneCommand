# KitsunePvPBalance

Server-side PvP damage rebalancing mod for 7 Days to Die. Configurable multiplier that scales player-vs-player damage without affecting PvE combat.

## Features

- **Configurable damage multiplier** — reduce PvP damage to extend fights (default: 50%)
- **Headshot multiplier** — separate multiplier for critical hits
- **Zero-restart config** — change settings via console command, no server restart needed
- **PvP hit logging** — optional per-hit logging for debugging and balancing
- **KitsuneCommand compatible** — works standalone or alongside KC (auto-detects, no conflicts)

## Installation

1. Download the latest `KitsunePvPBalance.zip` from [Releases](https://github.com/AdaInTheLab/KitsuneCommand/releases)
2. Extract the `KitsunePvPBalance` folder into your server's `Mods/` directory
3. Restart the server

## Configuration

Edit `Mods/KitsunePvPBalance/Config/pvpbalance.json`:

```json
{
  "Enabled": true,
  "DamageMultiplier": 0.5,
  "HeadshotMultiplier": 1.0,
  "LogPvPHits": false
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Master toggle for PvP damage modification |
| `DamageMultiplier` | `0.5` | PvP damage scale (0 = disabled, 0.5 = half, 1.0 = vanilla, 2.0 = double) |
| `HeadshotMultiplier` | `1.0` | Additional multiplier for critical/headshot hits |
| `LogPvPHits` | `false` | Log each PvP hit to server console (useful for tuning) |

## Console Commands

| Command | Description |
|---------|-------------|
| `kpvp` | Show current settings |
| `kpvp set multiplier 0.3` | Change damage multiplier |
| `kpvp set headshot 1.5` | Change headshot multiplier |
| `kpvp set enabled false` | Disable PvP modification |
| `kpvp set log true` | Enable hit logging |
| `kpvp reload` | Reload settings from config file |

## How It Works

Uses a Harmony patch on `EntityAlive.DamageEntity` to intercept damage events. When both the attacker and victim are players, the damage amount is multiplied by the configured value before being applied. PvE damage (zombies, animals, environment) is completely unaffected.

## KitsuneCommand Integration

If you also run [KitsuneCommand](https://github.com/AdaInTheLab/KitsuneCommand), the PvP balance settings are automatically available through the web panel API at `GET/PUT /api/pvp/settings`. You don't need both mods installed — KC includes the same functionality built-in. Only install the standalone version if you want PvP balance **without** the full KC suite.
