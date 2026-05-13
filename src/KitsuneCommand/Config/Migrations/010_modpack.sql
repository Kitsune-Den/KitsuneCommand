-- Singleton modpack table for the server's player-facing mod bundle.
--
-- "Latest version only" semantics (per the spec): there's ever only ONE row.
-- Editing updates it in place; publishing replaces the previously-published
-- zip on disk; archiving keeps the metadata but unlists from the public
-- download CTA on the login page. Deleting drops the row + the zip together.
--
-- status:
--   'draft'     — admin is preparing a new pack (not visible publicly)
--   'published' — visible on the login-page Download CTA, public download enabled
--   'archived'  — kept for reference but not visible / downloadable
CREATE TABLE IF NOT EXISTS modpack (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT    NOT NULL,
    version         TEXT    NOT NULL,
    status          TEXT    NOT NULL DEFAULT 'draft',
    -- Filename of the built zip, relative to the modpacks dir (resolved at
    -- runtime via ModpackService.GetModpackDir). NULL until BuildZip runs.
    filename        TEXT,
    size_bytes      INTEGER NOT NULL DEFAULT 0,
    mod_count       INTEGER NOT NULL DEFAULT 0,
    -- JSON array of mod folder names included in the pack.
    -- e.g. ["KitsunePaintUnlocked","SomeOtherMod"]
    mod_list        TEXT    NOT NULL DEFAULT '[]',
    description     TEXT,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    download_count  INTEGER NOT NULL DEFAULT 0
);
