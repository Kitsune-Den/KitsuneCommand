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

If even the game server won't boot, drop a `RESET_PASSWORD.txt` in the
save-game `KitsuneCommand/` folder containing the new password — KC will
verify against it on next login attempt and rotate the hash.

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
