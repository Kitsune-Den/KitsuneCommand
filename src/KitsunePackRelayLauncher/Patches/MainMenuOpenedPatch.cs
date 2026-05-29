using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace KitsunePackRelayLauncher.Patches
{
    /// <summary>
    /// Postfix on <c>XUiC_MainMenu.OnOpen()</c> -- the moment the
    /// main menu UI becomes visible after the splash dismisses.
    /// Found via Mono.Cecil reflection on Assembly-CSharp during the
    /// 2026-05-28 spelunk session (see vault runbook
    /// `packrelay-quickjoin-mod-plan.md`).
    ///
    /// v0.1 (this patch): reads the sentinel file, freshness-checks
    /// it (60s window), logs what it would do, deletes the file.
    /// **No actual connect.**
    ///
    /// v0.2: after this logs "would auto-connect," fill in the UI
    /// driving code:
    ///   1. Locate the <c>XUiC_ServerBrowserDirectConnect</c> window
    ///      in the live XUi tree.
    ///   2. Set its <c>txtIp</c> / <c>txtPort</c> field text values.
    ///   3. Invoke <c>btnDirectConnectConnect</c>'s OnPressed event
    ///      (the same delegate the actual button click fires).
    ///
    /// Sentinel file convention (written by the PackRelay launcher
    /// before spawning 7DTD):
    ///
    ///   <c>&lt;userdatafolder&gt;/packrelay-quickjoin.json</c>
    ///   {
    ///     "host":      "play.kitsuneden.net",
    ///     "port":      26900,
    ///     "writtenAt": "2026-05-28T19:30:00Z"
    ///   }
    ///
    /// Freshness gate is 60s: an interrupted launch followed by the
    /// user manually opening the game later shouldn't surprise-join,
    /// and a sentinel from yesterday shouldn't fire today.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_MainMenu), nameof(XUiC_MainMenu.OnOpen))]
    public static class MainMenuOpenedPatch
    {
        /// <summary>
        /// Tracks whether we've already fired this 7DTD session. The
        /// main menu can re-open (player quits a server back to menu;
        /// XUiC_MainMenu.OnOpen fires again). We only want auto-join
        /// to fire on the FIRST main-menu-ready event of the process,
        /// not every time the menu reappears.
        /// </summary>
        private static bool _firedThisSession;

        private const int FreshnessSeconds = 60;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_firedThisSession) return;
            _firedThisSession = true;

            try
            {
                string sentinelPath = ResolveSentinelPath();
                if (string.IsNullOrEmpty(sentinelPath) || !File.Exists(sentinelPath))
                {
                    // No sentinel = normal launch path. Quiet -- this
                    // is the common case and shouldn't spam the log.
                    return;
                }

                string raw;
                try
                {
                    raw = File.ReadAllText(sentinelPath);
                }
                catch (Exception ex)
                {
                    Log.Warning("[KitsunePackRelayLauncher] Couldn't read sentinel " +
                                sentinelPath + ": " + ex.Message);
                    return;
                }

                if (!TryParseSentinel(raw, out string host, out int port, out DateTime writtenAt))
                {
                    Log.Warning("[KitsunePackRelayLauncher] Sentinel file malformed (raw='" +
                                Truncate(raw, 200) + "'); deleting and ignoring.");
                    TryDelete(sentinelPath);
                    return;
                }

                double ageSec = (DateTime.UtcNow - writtenAt.ToUniversalTime()).TotalSeconds;
                if (ageSec > FreshnessSeconds)
                {
                    Log.Out("[KitsunePackRelayLauncher] Sentinel is stale (" +
                            ageSec.ToString("F1") + "s old, gate=" + FreshnessSeconds +
                            "s). Ignoring + deleting so it doesn't fire on a future " +
                            "manual launch.");
                    TryDelete(sentinelPath);
                    return;
                }

                // STUB: v0.1 logs what it would do, then deletes the
                // sentinel so the launch sequence is clean for v0.2.
                Log.Out(
                    "\n" +
                    "================================================================\n" +
                    "[KitsunePackRelayLauncher] v0.1 stub: WOULD auto-connect now.\n" +
                    "  host:      " + host + "\n" +
                    "  port:      " + port + "\n" +
                    "  writtenAt: " + writtenAt.ToString("u") + "\n" +
                    "  age:       " + ageSec.ToString("F1") + "s\n" +
                    "\n" +
                    "  v0.2 will fill in the UI driving here:\n" +
                    "    1. Find XUiC_ServerBrowserDirectConnect in the XUi tree\n" +
                    "    2. Set its txtIp + txtPort field text\n" +
                    "    3. Fire btnDirectConnectConnect.OnPressed\n" +
                    "================================================================");

                TryDelete(sentinelPath);
            }
            catch (Exception ex)
            {
                // Never let our mod break the main-menu-open path --
                // the player needs to be able to use the game UI
                // regardless of what our mod thinks.
                Log.Warning("[KitsunePackRelayLauncher] Postfix threw: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves <c>&lt;userdatafolder&gt;/packrelay-quickjoin.json</c>.
        /// 7DTD exposes the user data folder via <c>GameIO.GetUserGameDataDir()</c>
        /// (returns the same value as <c>UserDataFolder</c> printed in
        /// the boot log). Falls back to null if the call throws -- in
        /// which case we just skip the sentinel check rather than
        /// guess at the path.
        /// </summary>
        private static string ResolveSentinelPath()
        {
            try
            {
                string userdata = GameIO.GetUserGameDataDir();
                if (string.IsNullOrEmpty(userdata)) return null;
                return Path.Combine(userdata, "packrelay-quickjoin.json");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Minimalist JSON parsing for the three fields we care
        /// about. Avoids pulling Newtonsoft into the mod's dep
        /// list -- the sentinel is a tiny three-field object,
        /// hand-parsing is fine.
        ///
        /// Format expected (whitespace tolerant):
        ///   { "host": "...", "port": NNN, "writtenAt": "ISO8601" }
        /// </summary>
        private static bool TryParseSentinel(
            string raw,
            out string host,
            out int port,
            out DateTime writtenAt)
        {
            host = null;
            port = 0;
            writtenAt = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(raw)) return false;

            host = ExtractStringField(raw, "host");
            string portStr = ExtractRawField(raw, "port");
            string writtenAtStr = ExtractStringField(raw, "writtenAt");

            if (string.IsNullOrEmpty(host)) return false;
            if (!int.TryParse(portStr, out port) || port <= 0 || port > 65535) return false;
            if (!DateTime.TryParse(
                    writtenAtStr,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out writtenAt))
            {
                return false;
            }
            return true;
        }

        /// <summary>Extract <c>"key": "value"</c> from a JSON-ish blob.</summary>
        private static string ExtractStringField(string raw, string key)
        {
            // Quick-and-dirty regex would be cleaner but we're keeping
            // the dep surface minimal. Find `"key"`, skip ahead to the
            // first `"`, read until next unescaped `"`.
            string marker = "\"" + key + "\"";
            int i = raw.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            i = raw.IndexOf(':', i + marker.Length);
            if (i < 0) return null;
            i = raw.IndexOf('"', i + 1);
            if (i < 0) return null;
            int end = raw.IndexOf('"', i + 1);
            if (end < 0) return null;
            return raw.Substring(i + 1, end - i - 1);
        }

        /// <summary>Extract <c>"key": <em>token</em></c> (unquoted token) from JSON-ish.</summary>
        private static string ExtractRawField(string raw, string key)
        {
            string marker = "\"" + key + "\"";
            int i = raw.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            i = raw.IndexOf(':', i + marker.Length);
            if (i < 0) return null;
            i++;
            // Skip whitespace
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            int start = i;
            while (i < raw.Length && (char.IsDigit(raw[i]) || raw[i] == '-' || raw[i] == '+'))
            {
                i++;
            }
            if (i == start) return null;
            return raw.Substring(start, i - start);
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warning("[KitsunePackRelayLauncher] Couldn't delete sentinel " +
                            path + ": " + ex.Message);
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
