using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;

namespace KitsunePackRelayLauncher.Patches
{
    /// <summary>
    /// Postfix on <c>XUiC_MainMenu.OnOpen()</c> -- the moment the
    /// main menu UI becomes visible after the splash dismisses.
    /// Found via Mono.Cecil reflection on Assembly-CSharp during the
    /// 2026-05-28 spelunk session (see vault runbook
    /// `packrelay-quickjoin-mod-plan.md`).
    ///
    /// Reads the sentinel file, checks it belongs to this launch,
    /// deletes it, and hands the address to
    /// <see cref="DirectConnectDriver"/>, which joins through the
    /// game's own Direct Connect dialog (7DTD 3.x).
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
    /// Freshness gate: the sentinel has to have been written in the
    /// 60s before this game process started. An interrupted launch
    /// followed by the user manually opening the game later shouldn't
    /// surprise-join, and a sentinel from yesterday shouldn't fire
    /// today. It's measured against process start rather than "now"
    /// because a big modpack can take well over a minute to reach the
    /// main menu, which would make every real launch look stale.
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

        /// <summary>
        /// Slack for a sentinel stamped a moment after the process
        /// started (the launcher writing it just after spawn) or for
        /// clock jitter between the launcher and the game.
        /// </summary>
        private const int AfterStartSlackSeconds = 10;

        /// <summary>
        /// Used only if the process start time can't be read: then
        /// "now" is the anchor, and it has to allow for the boot.
        /// </summary>
        private const int FallbackFreshnessSeconds = 300;

        [HarmonyPostfix]
        public static void Postfix(XUiC_MainMenu __instance)
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

                // Delete before acting, so nothing below can leave a
                // sentinel behind to fire on a later launch.
                TryDelete(sentinelPath);

                if (!IsFromThisLaunch(writtenAt.ToUniversalTime(), out string why))
                {
                    Log.Out("[KitsunePackRelayLauncher] Sentinel for " + host + ":" + port +
                            " isn't from this launch (" + why + "). Ignored and deleted.");
                    return;
                }

                Log.Out("[KitsunePackRelayLauncher] Sentinel for " + host + ":" + port +
                        " is from this launch (" + why + "). Joining.");
                DirectConnectDriver.Start(__instance, host, port);
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
        /// True when the sentinel was written for the launch that
        /// started this process: within <see cref="FreshnessSeconds"/>
        /// before process start, or up to
        /// <see cref="AfterStartSlackSeconds"/> after it. If the start
        /// time can't be read, falls back to the sentinel's age against
        /// now with the looser <see cref="FallbackFreshnessSeconds"/>.
        /// <paramref name="why"/> says which check ran, for the log.
        /// </summary>
        private static bool IsFromThisLaunch(DateTime writtenAtUtc, out string why)
        {
            DateTime? startedUtc = null;
            try
            {
                startedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            }
            catch
            {
                // Mono on some platforms can't read it; use the fallback.
            }

            if (startedUtc.HasValue)
            {
                double beforeStart = (startedUtc.Value - writtenAtUtc).TotalSeconds;
                why = "written " + beforeStart.ToString("F1") + "s before the game started, " +
                      "window " + FreshnessSeconds + "s";
                return beforeStart <= FreshnessSeconds && beforeStart >= -AfterStartSlackSeconds;
            }

            double age = (DateTime.UtcNow - writtenAtUtc).TotalSeconds;
            why = age.ToString("F1") + "s old, process start unknown, window " +
                  FallbackFreshnessSeconds + "s";
            return age <= FallbackFreshnessSeconds && age >= -AfterStartSlackSeconds;
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
