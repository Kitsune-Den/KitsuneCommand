# Troubleshooting

A lookup index for prod-only failures we've actually hit. Each entry is
shaped **symptom → cause → fix** because operators scan for the symptom,
not the diagnosis.

If you fix something not in this list, please add it. The pattern is in
the [Adding an entry](#adding-an-entry) section at the bottom — should
take less than a minute.

---

## "I'm locked out of the panel."

**Cause:** Forgotten password, or password change went sideways. Whatever
the reason, you have admin access to the server itself, so you don't need
the panel to recover the panel.

**Fix:** Run `kcresetpw <username> <newpassword>` from any of:

- The 7D2D game console (in-server)
- Telnet — `nc localhost 8081` on the server box
- KC's own Console view, once it's reconnected

Min 8 characters, admin-only. The plaintext is hashed before write
and is not logged. (PR [#37](https://github.com/AdaInTheLab/KitsuneCommand/pull/37).)

If you can't reach any console, drop a `RESET_PASSWORD.txt` containing
the new password into KC's data folder, then sign in with your username
and that password. KC checks the file when a sign-in fails, re-hashes the
password and deletes the file. The server has to be running for this.

KC's data folder sits next to the game's `Saves` folder, not inside a world
save:

- Windows: `%APPDATA%\7DaysToDie\KitsuneCommand\`
- Linux: `~/.local/share/7DaysToDie/KitsuneCommand/`
- Or `<UserDataFolder>/KitsuneCommand/` if `UserDataFolder` is set in
  serverconfig.xml.

---

## "Fresh install: admin / admin doesn't work."

**Cause:** There's no default password. On its first boot KC creates the
`admin` account with a random one.

**Fix:** It's printed in the server console/log from that first boot (the
block marked `FIRST RUN`) and written to `FIRST_RUN_PASSWORD.txt` in KC's
data folder (see above). Lost both? Use `kcresetpw admin <newpassword>`.
The login page's **Can't sign in?** link says the same since v2.10.0
([#110](https://github.com/Kitsune-Den/KitsuneCommand/issues/110)).

---

## "I changed my password in Settings and now can't log in."

**Cause:** A bug in `AuthService.ChangePassword` was calling a generic
`Update` that doesn't persist `password_hash` — so the new password
appeared to save but never actually wrote. Fixed in
PR [#36](https://github.com/AdaInTheLab/KitsuneCommand/pull/36)
by calling `IUserAccountRepository.UpdatePassword` directly.

**Fix:** If you're on a build older than PR #36, use `kcresetpw` (above)
to recover, then update KC. Newer builds: this can't happen.

---

## "Sign in through Steam" fails or sends me to the wrong address

**Cause:** Steam sends you back to the **Panel address** saved under
Settings → Users, and nowhere else. If that address isn't the one in your
browser's address bar (a different hostname, `http` vs `https`, a missing
port, or a stale address after moving behind a tunnel), Steam returns you
somewhere else, or the sign-in can't finish in the browser that started it.

**Fix:** Sign in with your password, open Settings → Users, and set Panel
address to exactly what you type to reach the panel (no trailing `#/...`),
e.g. `https://panel.example.com` or `http://203.0.113.5:8890`. Then check
what the login page says:

- **"isn't linked to a panel account"**: Steam worked, but no user has
  linked that Steam account yet. Sign in with the password and use
  Settings → Account → **Link Steam account**.
- **"Couldn't reach Steam"**: the game server can't make outbound HTTPS
  requests to `steamcommunity.com`. Check its firewall or egress rules.
- **"has to finish in the same browser"**: the sign-in started and ended
  in different browsers or profiles, or the browser blocked the
  `kc_steam_state` cookie. Start again from the panel's own address.

Locked out with Steam not working? Passwords still work, and so does
`kcresetpw`.

---

## Live badge never turns green; WebSocket shows "Finished 0 kB"

**Cause:** WebSocketSharp's `WebSocketServer` does strict Host-header
validation — any request whose `Host` doesn't match the authority it
was bound to gets HTTP 400 *before* `OnOpen` runs. So token-validation
logging never fires either, and the symptom from the browser side is
indistinguishable from "WebSocket happily upgraded then closed."

When KC is fronted by Cloudflared, the tunnel by default forwards the
public hostname (e.g. `Host: panel.example.com`) which doesn't match
KC's local bind (`0.0.0.0:8889`). The WS upgrade fails at the very
first byte and you're stuck with an Offline badge that never flips.

**Fix:** Set `originRequest.httpHostHeader: "localhost:8889"` on the WS
ingress rule in `/etc/cloudflared/config.yml`, then
`sudo systemctl restart cloudflared`. See
[`cloudflared-tunnel.example.yml`](cloudflared-tunnel.example.yml) in this
folder for the proven full config. (PR [#38](https://github.com/AdaInTheLab/KitsuneCommand/pull/38).)

Two sharp edges that will get you if you wing it:

1. The value must be in `host:port` form. Bare `localhost` still gets
   rejected by WebSocketSharp's check.
2. Use `127.0.0.1` (not `localhost`) in the `service:` URL above —
   cloudflared resolves `localhost` to `::1` first, and KC's WS server
   only binds `0.0.0.0` (IPv4).

If you're tempted to "fix" this by renaming the WebSocket path away from
`/ws` because you're seeing 400s with `Server: cloudflare` in the
response: don't. Cloudflare stamps `Server: cloudflare` on every proxied
response, including origin-rejected ones, so an origin-side WebSocketSharp
400 looks identical to a CF-edge WAF block. Fix the Host header.

---

## `MapTileRenderer initialization failed: ... dl assembly:<unknown>`

**Cause:** SkiaSharp's `LibraryLoader` declares `[DllImport("dl")]`
(short name), and 7D2D's bundled Mono config has no `dllmap` for that
short name — only for `libdl` / `libdl.so.2`. Static initializer of
`SkiaSharp.SKData` throws on the first load.

**Fix:** Ship `SkiaSharp.dll.config` alongside `SkiaSharp.dll` with:

```xml
<configuration>
  <dllmap dll="dl" target="libdl.so.2" os="!windows" />
</configuration>
```

Mirrors the existing `System.Data.SQLite.dll.config`. The build script
copies it automatically. (PR [#34](https://github.com/AdaInTheLab/KitsuneCommand/pull/34).)

---

## SkiaSharp loads, but throws at runtime / Map view is blank

**Cause:** SkiaSharp's `GetLibraryPath` on glibc Linux looks for
`{assemblyDir}/x64/libSkiaSharp.so`. Older build scripts copied to
`linux-x64/` (matching .NET runtime conventions), which SkiaSharp
doesn't check.

**Fix:** Build scripts must copy `libSkiaSharp.so` to `x64/`, not
`linux-x64/`. (PR [#34](https://github.com/AdaInTheLab/KitsuneCommand/pull/34).)
Verify on prod with:

```bash
ls Mods/KitsuneCommand/x64/libSkiaSharp.so
file Mods/KitsuneCommand/x64/libSkiaSharp.so   # should be x86_64
```

If it says ARM or i386, you copied the wrong NuGet runtime — pull from
`runtimes/linux-x64/native/` of the SkiaSharp NuGet package.

---

## `cloudflared` reports `connect: connection refused` on `[::1]:8889`

**Cause:** Cloudflared resolves `localhost` to IPv6 (`::1`) first.
WebSocketSharp's server binds to `0.0.0.0` (IPv4 only). The connect
fails before any HTTP is ever exchanged.

**Fix:** Use `http://127.0.0.1:8889` explicitly in
`/etc/cloudflared/config.yml` — never `http://localhost:8889`. The
example config in [`cloudflared-tunnel.example.yml`](cloudflared-tunnel.example.yml)
already does this.

---

## Favicon / login logo missing after a frontend deploy

**Cause:** `wwwroot/` on prod gets fully replaced on each frontend
deploy. If the deploy command only copied `assets/` (the hashed JS/CSS),
top-level files like `favicon.svg`, `kitsune-command-logo-transparent.png`,
and `index.html`'s `<link>` references silently disappear.

**Fix:** Deploy the entire `wwwroot/` directory, not just `assets/`.
The build script (`tools/build.ps1`) emits everything to
`src/KitsuneCommand/wwwroot/` — copy the whole folder, or use
`scp -r src/KitsuneCommand/wwwroot/* <prod>:.../wwwroot/`. If you
need to clear stale hashed bundles before upload (recommended), `rm -f
.../wwwroot/assets/*.js .../wwwroot/assets/*.css` on prod first.

---

## My server shows `N/A` ping and a blank description in the in-game server browser

(Your server still appears in the list, you can connect to it, gameplay
is fine — the *display* in the browser is just empty.)

**Cause (confirmed):** 7D2D V 2.x switched the server browser's discovery
mechanism from Steam Query to **EOS** (Epic Online Services). Steam-side
ports `26901` and `26903` are not bound by the dedicated server anymore,
**by design**. `sudo ss -tulnp | grep ':269'` will only show `26900` and
`26902`. The server logs make this clear:

```
[Platform] Using cross platform: EOS
[EOS] Initialize: Success
[EOS] Registering server
[EOS] Server registered, session: <id>, 56 attributes   ← description, world,
[Steamworks.NET] GameServer.Init successful                 player count, etc.
[Steamworks.NET] GameServer.LogOn successful, SteamID=...   are bundled in here
```

So the server *is* properly published, with its description and stats
sent to EOS as session attributes. **There is no missing port to bind
and no config to fix on the server side.**

**Cause (inferred, not yet fully verified):** the `N/A` and blank
description in the browser is something downstream of EOS — either an
EOS lookup miss for some subset of sessions, or a vanilla client-side
rendering bug for certain attribute combinations. Hits other operators'
servers too, not unique to KC. Tends to come and go.

**"Fix":** there isn't one on your end. Don't go chasing a missing
`26901` bind or editing `serverconfig.xml`; you'll burn an afternoon for
nothing. The server is correctly registered. To confirm, look for the
EOS / GameServer lines above in `~/7d2d-server/output_log__*.txt`:

```bash
grep -iE "GameServer|Server registered|EOS|MasterServer" \
  $(ls -t ~/7d2d-server/output_log__*.txt | head -1) | head -10
```

If those lines are present and successful, your server is fine — direct-
IP connect works, KC's panel-side dashboard keeps showing live stats
(it doesn't route through the public browser at all), and the cosmetic
display issue tends to resolve in a TFP patch or as EOS catches up.

If `[EOS] Initialize` *fails* in the log, that's a different and real
problem — EOS SDK version mismatch, network egress to EOS blocked, etc.
Out of scope for this entry but worth flagging in a fresh card.

---

## Adding an entry

Lower the friction so this list keeps growing alongside the bug fixes:

1. New entry goes at the bottom of the relevant section (or just at the
   bottom of the file — order can come later).
2. **Symptom heading** is what an operator would type into Ctrl-F.
   Use words from the actual error message or visible UI state, not
   the underlying technical cause. "Live badge never turns green" beats
   "WebSocket Upgrade returns 400."
3. **Cause** is one or two sentences. Link the PR if there is one.
4. **Fix** is the actual command or config snippet. Include verification
   if it's quick.
5. Skip prose essays. This is a lookup index, not a narrative. The
   PR description is where the long-form story lives.

When you fix a gnarly prod bug, "update `docs/troubleshooting.md`"
should be the obvious last commit of the fix PR.
