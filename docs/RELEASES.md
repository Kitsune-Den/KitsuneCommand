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

Tag-push triggers `.github/workflows/release.yml`. It builds, signs,
and creates a draft GitHub release with three assets. Total maintainer
work per release: ~5 minutes of edits + a Publish click after CI lands.

### 1. Bump version

Three places (a future `tools/bump-version.{sh,ps1}` will do this in
one shot — kanban #137):

- `src/KitsuneCommand/ModInfo.xml` (the version 7DTD displays)
- `src/KitsuneCommand/KitsuneCommand.csproj` `<Version>` (if present)
- `frontend/package.json` `"version"`

### 2. Update CHANGELOG.md

Move the `[Unreleased]` entries into a new `[X.Y.Z] - YYYY-MM-DD`
section at the top. Add a compare link at the bottom (template at the
end of the file).

The release workflow extracts the matching `## [X.Y.Z]` section as the
GitHub release body, so write it like someone's actually going to read
it on the release page.

### 3. Tag + push

```bash
git tag -a vX.Y.Z -m "vX.Y.Z — short headline"
git push origin main
git push origin vX.Y.Z
```

GitHub Actions catches the tag push and runs `release.yml`. Watch the
run at <https://github.com/Kitsune-Den/KitsuneCommand/actions>.

The workflow:

1. Builds the frontend + the .NET mod via `tools/build.ps1`
2. Packages `dist/KitsuneCommand` as `KitsuneCommand-vX.Y.Z.zip`
3. Computes the sha256 → writes `KitsuneCommand-vX.Y.Z.zip.sha256`
4. Signs the zip with minisign → writes `KitsuneCommand-vX.Y.Z.zip.minisig`
5. Extracts the matching `## [X.Y.Z]` section from CHANGELOG.md
6. Creates a **draft** GitHub Release with all three assets attached

Pre-release tags (`vX.Y.Z-rc.N`, `vX.Y.Z-beta.N`) get marked as
prerelease automatically.

### 4. Smoke-test the draft

The release is a draft on purpose — that's the seam where you eyeball
the notes, attach screenshots / migration notes, and verify the artifact
before publishing.

```bash
# From a clean dir, fetch all three from the draft release:
gh release download vX.Y.Z --dir /tmp/kc-release-check

# Verify integrity + authenticity
cd /tmp/kc-release-check
sha256sum -c KitsuneCommand-vX.Y.Z.zip.sha256
minisign -Vm KitsuneCommand-vX.Y.Z.zip -P <public-key-below>
```

Both must pass. If either fails, the release is poisoned somehow — open
an issue, do NOT publish the draft.

### 5. Hit Publish

Open the draft at <https://github.com/Kitsune-Den/KitsuneCommand/releases>,
edit notes if you want, click Publish. The release goes live + the
"latest" tag updates.

### 6. Deploy to your own server (the existing flow)

`tools/deploy.{sh,ps1}` handles this. See `docs/DEPLOYING.md` for the
full walkthrough.

## Verifying a release

Every release zip ships with two sidecar files: a SHA-256 sum and a
minisign Ed25519 signature. **As a downloader, verifying both before
extracting is the recommended path** — it confirms the bytes are
byte-identical to what GitHub Actions built (sha256) AND signed by the
KC maintainer (minisign).

You'll need [minisign](https://jedisct1.github.io/minisign/) installed:

| Platform | Install |
|----------|---------|
| Linux (Debian/Ubuntu) | `sudo apt install minisign` |
| Linux (Fedora/RHEL)   | `sudo dnf install minisign` |
| macOS                 | `brew install minisign` |
| Windows               | `choco install minisign -y` · or `scoop install minisign` · or download from [jedisct1/minisign releases](https://github.com/jedisct1/minisign/releases) |

### Verify command

Download `KitsuneCommand-vX.Y.Z.zip`, `.sha256`, and `.minisig` from the
release page, then:

```bash
sha256sum -c KitsuneCommand-vX.Y.Z.zip.sha256
minisign -Vm KitsuneCommand-vX.Y.Z.zip -P <PUBLIC-KEY-PLACEHOLDER>
```

If `minisign -V` prints `Signature and comment signature verified`,
you're good. Anything else — don't extract the zip. Open an issue.

### Public key

```
<PUBLIC-KEY-PLACEHOLDER>
```

(Replaced with the real key once the keypair is generated. See
[`docs/SIGNING.md`](SIGNING.md) for the one-time setup walkthrough.)

For the rationale (why minisign, key rotation, common failure modes),
see [`docs/SIGNING.md`](SIGNING.md).

## Manual fallback (if CI is down)

If GitHub Actions is unavailable and you need to cut a release manually,
the workflow's logic is mirrored in two scripts:

```bash
# 1. Build (PowerShell on Windows, bash on Linux)
tools/build.ps1                  # or tools/build.sh

# 2. Zip
Compress-Archive -Path dist/KitsuneCommand -DestinationPath dist/KitsuneCommand-vX.Y.Z.zip

# 3. Sign + checksum (graceful skip if minisign key isn't on disk)
tools/sign-release.ps1 -ZipPath dist/KitsuneCommand-vX.Y.Z.zip
#   or
tools/sign-release.sh dist/KitsuneCommand-vX.Y.Z.zip

# 4. Create release with all three assets
gh release create vX.Y.Z \
    --title "vX.Y.Z — short headline" \
    --notes-file CHANGELOG-section.md \
    dist/KitsuneCommand-vX.Y.Z.zip \
    dist/KitsuneCommand-vX.Y.Z.zip.sha256 \
    dist/KitsuneCommand-vX.Y.Z.zip.minisig
```

The manual path produces identical outputs to the CI path. Local
signing needs `~/.keys/kc-minisign.key` present (see `docs/SIGNING.md`
for setup).
