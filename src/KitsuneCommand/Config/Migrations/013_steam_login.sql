-- Sign in through Steam: a panel account can be linked to one SteamID64.
-- NULL = not linked. The unique index stops one Steam account from being
-- linked to two panel accounts (SQLite allows any number of NULLs in it).
ALTER TABLE user_accounts ADD COLUMN steam_id TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_accounts_steam_id ON user_accounts(steam_id);
