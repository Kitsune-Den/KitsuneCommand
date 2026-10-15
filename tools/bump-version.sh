#!/usr/bin/env bash
#
# Bump KitsuneCommand's version everywhere it lives, and roll the CHANGELOG.
# Bash twin of tools/bump-version.ps1; same behavior, same refusals.
#
# Step 1 + 2 of docs/RELEASES.md in one shot:
#
#   src/KitsuneCommand/ModInfo.xml   <Version value="X.Y.Z" />  (what 7DTD shows)
#   frontend/package.json            "version"
#   frontend/package-lock.json       "version" + packages[""].version
#   CHANGELOG.md                     [Unreleased] entries move under a new
#                                    "## [X.Y.Z] - YYYY-MM-DD" heading with the
#                                    "> [Full notes](...)" line, and the compare
#                                    links at the bottom get updated
#
# Refuses when the three version files disagree, when the new version isn't
# higher than the current one, when the tag already exists, or when
# [Unreleased] is empty (unless --allow-empty). Doesn't commit, tag or push:
# review the diff, write a short summary under "> [Full notes]", then follow
# docs/RELEASES.md from step 3.
#
# Needs node + npm (the frontend build already does). File edits go through
# node so they behave the same on Linux and macOS and keep CRLF files CRLF.

set -euo pipefail

usage() {
    cat <<EOF
Usage: $0 <version> [--date YYYY-MM-DD] [--dry-run] [--allow-empty]
       $0 --check

  <version>       X.Y.Z, or X.Y.Z-rc.N / X.Y.Z-beta.N for a prerelease
  --date          release date for the CHANGELOG heading (default: today)
  --dry-run       show what would change without writing anything
  --allow-empty   cut it even if [Unreleased] is empty (e.g. final after an rc)
  --check         only check the version files agree; exit 1 if they don't
EOF
    exit 2
}

fail() { echo "bump-version: $*" >&2; exit 1; }

VERSION=""
DATE="$(date +%Y-%m-%d)"
DRY_RUN=0
ALLOW_EMPTY=0
CHECK=0
while [ $# -gt 0 ]; do
    case "$1" in
        --date) [ $# -ge 2 ] || usage; DATE="$2"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
        --allow-empty) ALLOW_EMPTY=1; shift ;;
        --check) CHECK=1; shift ;;
        -h|--help) usage ;;
        -*) echo "Unknown option: $1" >&2; usage ;;
        *) [ -z "$VERSION" ] || usage; VERSION="$1"; shift ;;
    esac
done
if [ "$CHECK" = 1 ]; then [ -z "$VERSION" ] || usage; else [ -n "$VERSION" ] || usage; fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODINFO="$ROOT/src/KitsuneCommand/ModInfo.xml"
PACKAGE_JSON="$ROOT/frontend/package.json"
PACKAGE_LOCK="$ROOT/frontend/package-lock.json"
CHANGELOG="$ROOT/CHANGELOG.md"
REPO_URL="https://github.com/Kitsune-Den/KitsuneCommand"

command -v node >/dev/null || fail "node isn't on PATH."

# Prints "label<TAB>version" for each place the version lives.
read_versions() {
    node - "$MODINFO" "$PACKAGE_JSON" "$PACKAGE_LOCK" <<'JS'
const fs = require('fs');
const [, , modInfo, pkgPath, lockPath] = process.argv;
const mod = /<Version\s+value="([^"]+)"/.exec(fs.readFileSync(modInfo, 'utf8'));
const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
const rows = [
  ['src/KitsuneCommand/ModInfo.xml', mod ? mod[1] : '(missing)'],
  ['frontend/package.json', pkg.version],
  ['frontend/package-lock.json', lock.version],
  ['frontend/package-lock.json packages[""]', ((lock.packages || {})[''] || {}).version],
];
for (const [label, v] of rows) console.log(`${label}\t${v}`);
JS
}

VERSIONS="$(read_versions)"
DISTINCT="$(printf '%s\n' "$VERSIONS" | cut -f2 | sort -u)"
if [ "$(printf '%s\n' "$DISTINCT" | wc -l | tr -d ' ')" != 1 ]; then
    echo "Version files disagree:" >&2
    printf '%s\n' "$VERSIONS" | awk -F'\t' '{ printf "  %-45s %s\n", $1, $2 }' >&2
    fail "make them match by hand, then re-run."
fi
CURRENT="$DISTINCT"

if [ "$CHECK" = 1 ]; then
    echo "Version files agree: $CURRENT"
    exit 0
fi

# X.Y.Z, X.Y.Z-rc.N, X.Y.Z-beta.N -> sortable key. A prerelease sorts below its release.
version_key() {
    if [[ "$1" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-(beta|rc)\.([0-9]+))?$ ]]; then
        local stage=2 n=0
        case "${BASH_REMATCH[5]}" in beta) stage=0 ;; rc) stage=1 ;; esac
        [ -n "${BASH_REMATCH[6]}" ] && n="${BASH_REMATCH[6]}"
        printf '%06d.%06d.%06d.%d.%06d' "$((10#${BASH_REMATCH[1]}))" "$((10#${BASH_REMATCH[2]}))" \
            "$((10#${BASH_REMATCH[3]}))" "$stage" "$((10#$n))"
    fi
}

NEW_KEY="$(version_key "$VERSION")"
CUR_KEY="$(version_key "$CURRENT")"
[ -n "$NEW_KEY" ] || fail "'$VERSION' isn't X.Y.Z, X.Y.Z-rc.N or X.Y.Z-beta.N."
[ -n "$CUR_KEY" ] || fail "current version '$CURRENT' isn't X.Y.Z, X.Y.Z-rc.N or X.Y.Z-beta.N."
[[ "$NEW_KEY" > "$CUR_KEY" ]] || fail "$VERSION isn't higher than the current $CURRENT."
[[ "$DATE" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || fail "--date '$DATE' isn't YYYY-MM-DD."
[ -z "$(git -C "$ROOT" tag --list "v$VERSION")" ] || fail "tag v$VERSION already exists."

# CHANGELOG + ModInfo.xml. Prints the previous version (from the [Unreleased]
# compare link), and a preview when dry-running. Exit 3 = a refusal, message on stderr.
roll_files() {
    node - "$CHANGELOG" "$MODINFO" "$VERSION" "$DATE" "$REPO_URL" "$DRY_RUN" "$ALLOW_EMPTY" <<'JS'
const fs = require('fs');
const [, , changelog, modInfo, version, date, repo, dryRun, allowEmpty] = process.argv;
const refuse = (msg) => { process.stderr.write(`bump-version: ${msg}\n`); process.exit(3); };

const log = fs.readFileSync(changelog, 'utf8');
const nl = log.includes('\r\n') ? '\r\n' : '\n';

const heading = /^## \[Unreleased\][ \t]*(?=\r?$)/m.exec(log);
if (!heading) refuse('no "## [Unreleased]" heading in CHANGELOG.md.');
const afterHeading = heading.index + heading[0].length;
const next = /^## \[/m.exec(log.slice(afterHeading));
const bodyEnd = next ? afterHeading + next.index : log.length;
const unreleased = log.slice(afterHeading, bodyEnd);
if (!unreleased.trim() && allowEmpty !== '1')
  refuse('[Unreleased] in CHANGELOG.md is empty: nothing to release. (--allow-empty cuts it anyway, e.g. a final release after an rc.)');

const link = /^\[Unreleased\]: \S+\/compare\/v(\S+)\.\.\.HEAD[ \t]*(?=\r?$)/m.exec(log);
if (!link) refuse('no "[Unreleased]: .../compare/vX.Y.Z...HEAD" link at the bottom of CHANGELOG.md.');
const previous = link[1];

const newLinks = `[Unreleased]: ${repo}/compare/v${version}...HEAD${nl}[${version}]: ${repo}/compare/v${previous}...v${version}`;
const newSection = `## [Unreleased]${nl}${nl}## [${version}] - ${date}${nl}${nl}> [Full notes](${repo}/releases/tag/v${version})${nl}${nl}`;
const leading = unreleased.length - unreleased.replace(/^[\r\n]+/, '').length;

// The link is after the heading, so replace it first and the heading index stays valid.
let updated = log.slice(0, link.index) + newLinks + log.slice(link.index + link[0].length);
updated = updated.slice(0, heading.index) + newSection + updated.slice(afterHeading + leading);

console.log(previous);
if (dryRun === '1') {
  console.log(updated.slice(heading.index).split(/\r?\n/).slice(0, 8).join('\n'));
  process.exit(0);
}
fs.writeFileSync(changelog, updated);
const mod = fs.readFileSync(modInfo, 'utf8');
fs.writeFileSync(modInfo, mod.replace(/(<Version\s+value=")[^"]+(")/, (_, open, close) => open + version + close));
JS
}

set +e
ROLL_OUT="$(roll_files)"
ROLL_STATUS=$?
set -e
[ "$ROLL_STATUS" = 0 ] || exit 1
PREVIOUS="$(printf '%s\n' "$ROLL_OUT" | head -1)"

echo "KitsuneCommand $CURRENT -> $VERSION  (CHANGELOG: [$VERSION] - $DATE, compare v$PREVIOUS...v$VERSION)"
printf '%s\n' "$VERSIONS" | cut -f1 | sed 's/^/  /'
echo "  CHANGELOG.md"

if [ "$DRY_RUN" = 1 ]; then
    echo
    echo "Dry run: nothing written. The CHANGELOG would start:"
    printf '%s\n' "$ROLL_OUT" | tail -n +2 | sed 's/^/    /'
    exit 0
fi

(cd "$ROOT/frontend" && npm version "$VERSION" --no-git-tag-version --allow-same-version >/dev/null) \
    || fail "npm version failed."

AFTER="$(read_versions | cut -f2 | sort -u)"
[ "$AFTER" = "$VERSION" ] || fail "after the bump the version files read: $(printf '%s ' $AFTER)"

echo
echo "Bumped to $VERSION."
echo "Next:"
echo "  1. Under '> [Full notes]' in CHANGELOG.md, add a short '> ' summary for the release page (see earlier releases)."
echo "  2. Review: git diff"
echo "  3. Commit on release/v$VERSION, open the release PR, then tag v$VERSION after it merges (docs/RELEASES.md step 3)."
