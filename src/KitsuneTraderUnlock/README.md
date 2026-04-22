# KitsuneTraderUnlock

**Client-side companion mod for KitsuneCommand's BB-015 Trader Protection Toggle.**

The 7 Days to Die client refuses block edits in trader compounds before it even asks the server — so a server-only mod can't let admins clean up trader areas. This mod patches the client's local trader-area checks to return `false`, unlocking the UI so you can swing a pickaxe or place blocks inside trader zones.

## Security note

This mod **does not bypass server-side protection**. If the server rejects your edit (because it hasn't allowed trader edits), the block won't actually change — you'll just see the swing animation. Use alongside:

- **[KitsuneCommand](https://github.com/Kitsune-Den/KitsuneCommand)** on your server, then run `ktrader off` in the server console to unlock trader editing. Run `ktrader on` when cleanup is done.
- **Any server that allows trader edits** (e.g., a server running 0-SCore's trader protection disable).

Without a compatible server, installing this mod alone does nothing — the server will still reject your edits.

## Install

1. Download the latest release ZIP from [Releases](https://github.com/Kitsune-Den/KitsuneCommand/releases).
2. Extract `KitsuneTraderUnlock` into your game's `Mods/` folder (client or dedicated server).
3. Launch the game. Log shows `[KitsuneTraderUnlock] Harmony patches applied.`

Works on Windows and Linux.

## How it works

A handful of Harmony prefix patches force every trader-area check method to return `false`:

- `World.IsWithinTraderPlacingProtection(Vector3i)` / `(Bounds)`
- `World.IsWithinTraderArea(Vector3i)` / `(Vector3i, Vector3i)`
- `Chunk.IsTraderArea(int, int)`
- `TraderArea.IsWithinProtectArea(Vector3)`

No config file, no in-game command. Remove the mod folder to re-lock trader editing on your client.

## Companion to KitsuneCommand

Part of the [Kitsune Den](https://github.com/Kitsune-Den) mod suite. Server admins who want to control trader editing from a web panel should install [KitsuneCommand](https://github.com/Kitsune-Den/KitsuneCommand) on their server — it provides the `ktrader` console command and a REST API for toggling protection at runtime.
