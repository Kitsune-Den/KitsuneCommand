# Release Process

How KitsuneCommand versions itself + how a release gets cut. Two parts:
the [versioning convention](#versioning) (what each digit means) and the
[release process](#release-process) (the manual dance today, the automated
version in flight).

## Versioning

`MAJOR.MINOR.PATCH` — close to [semver](https://semver.org), with KC-shaped
edges where strict semver doesn't quite fit a 7DTD mod.

### MAJOR — `X.0.0`

Bumped when something that downstream users depend on has changed in a way
they can't ignore:

- The web panel's URL/port changes (v2.2.1's 8888 → 8890 would have been a
  major bump if v2.2.0 hadn't been DOA — instead it shipped as a patch
  because nobody could log in on v2.2.0 anyway).
- The SQLite schema gets a destructive migration (column dropped, table
  renamed) that requires admin action to upgrade.
- The mod's process-level interaction with 7DTD changes — e.g. switching
  the dedicated-server boot script contract, dropping support for a 7DTD
  major version.
- The minimum 7DTD version goes up. v2.x targets 7DTD V2.0+.

The full v1.x → v2.x bump was the clean-room V2 rewrite of ServerKit. The
next major (v3.0.0) will probably be the 7DTD V3 baseline cut.

### MINOR — `2.X.0`

Bumped when adding user-visible features that don't require a destructive
schema migration or a config rewrite. Most KC releases are minor:

- New web-panel page or feature (Vote Rewards, Modpack, Graceful Restart).
- New chat command, new console command.
- New supported locale.
- Substantial UX rework that doesn't break the JSON API.

### PATCH — `2.6.X`

Bug fixes + small polish that ride on top of the current MINOR. No new
user-facing features:

- Crash fix, race condition, silent-write-loss.
- Locale string corrections.
- Build / packaging fixes.
- Documentation, troubleshooting entries.

### Pre-release suffixes

`vX.Y.Z-rc.N` for release candidates, `vX.Y.Z-beta.N` for early feature
work. The (future) release workflow treats anything with a hyphen as a
GitHub prerelease — visible on the Releases page but not the Latest pill.

### Numbering quirks worth knowing

- **Skipped numbers are normal.** `v2.6.3` was bumped on `main` but never
  cut; the work folded into `v2.6.4`. `v2.3.0` + `v2.4.0` never existed —
  the cycle jumped from `v2.2.1` to `v2.5.0` when feature scope clarified.
- **Patch releases follow the same MINOR's `Changed`** items unless we say
  otherwise — i.e. `v2.6.1` keeps every feature `v2.6.0` shipped.

## Release process

Today (v2.6.4) the cut is manual. Card [#136](https://kitsunebi.cloud)
automates this; until then, the steps are:

### 1. Bump version

Three places (until [#137 lands a `tools/bump-version.{sh,ps1}`](https://kitsunebi.cloud)):

- `src/KitsuneCommand/ModInfo.xml` (the version 7DTD displays)
- `src/KitsuneCommand/KitsuneCommand.csproj` `<Version>` (if present)
- `frontend/package.json` `"version"`

Make sure the README's version pill (if any) refreshes — it usually pulls
from one of the above at build time, but worth eyeballing.

### 2. Update CHANGELOG.md

Move the `[Unreleased]` entries into a new `[X.Y.Z] - YYYY-MM-DD` section
at the top. Add a compare link at the bottom (template at the end of the
file).

Keep entries skimmable. The GitHub release page is where the prose lives;
CHANGELOG is what someone reads to answer "what's in this version?" in
under a minute.

### 3. Build the zip

```bash
# Linux (the supported path for production deploys)
tools/build.sh
```

```powershell
# Windows (dev convenience)
tools\build.ps1
```

Output: `dist/KitsuneCommand/`. Zip it as `KitsuneCommand-vX.Y.Z.zip`.

### 4. Tag + push

```bash
git tag -a vX.Y.Z -m "vX.Y.Z — short headline"
git push origin main
git push origin vX.Y.Z
```

The tag annotation message becomes the release title fallback if the
release notes page is empty when the workflow lands.

### 5. Create the GitHub release

```bash
gh release create vX.Y.Z \
  --title "vX.Y.Z — short headline" \
  --notes-file CHANGELOG-section.md \
  KitsuneCommand-vX.Y.Z.zip
```

Or via the GitHub web UI. The notes body should be the matching CHANGELOG
section, possibly enriched with screenshots / migration notes / "operators
upgrading from vX.Y.Z-1" specifics.

### 6. Deploy to your own server (smoke test)

```bash
scp -r dist/KitsuneCommand root@<your-box>:/path/to/7d2d-server/Mods/
ssh root@<your-box> "chown -R <user>:<group> /path/to/Mods/KitsuneCommand && systemctl restart 7daystodie"
```

Verify the panel loads, the new features work, no regressions. If
anything's broken: don't unpublish — cut a `vX.Y.Z+1` patch.

## Future state (cards [#136](https://kitsunebi.cloud) and [#138](https://kitsunebi.cloud))

Once the release workflow lands:

1. Bump versions (one `tools/bump-version` invocation)
2. Update CHANGELOG.md, commit
3. `git tag vX.Y.Z && git push --tags`
4. GHA produces a draft GitHub Release with the zip, SHA-256 sum, and
   minisign signature attached, body pulled from CHANGELOG.md
5. Hit Publish

End to end: ~5 minutes of human time + ~10 min of GHA build time. Today
it's ~30 min of careful manual ceremony.
