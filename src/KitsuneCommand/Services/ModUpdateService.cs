using System;
using System.Collections.Generic;
using System.Linq;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Per-mod result of a "check for updates" sweep. The frontend renders
    /// these in the installed-mods table — Status drives the badge color and
    /// Url is where the badge links (the matching Nexus or 7daystodiemods.com page).
    /// </summary>
    public class ModUpdateCheckResult
    {
        /// <summary>Local mod folder name (matches ModInfo.FolderName).</summary>
        public string FolderName { get; set; }

        /// <summary>"up_to_date" | "update_available" | "version_differs" | "no_match" | "skipped"</summary>
        public string Status { get; set; }

        public string InstalledVersion { get; set; }
        public string LatestVersion { get; set; }
        /// <summary>Where LatestVersion came from: "nexus" | "7daystodiemods".</summary>
        public string Source { get; set; }
        /// <summary>The matched listing's page.</summary>
        public string Url { get; set; }
        public int? NexusModId { get; set; }
        public string NexusUrl { get; set; }
        public string MatchedName { get; set; }
    }

    /// <summary>
    /// "Check for updates" backend. Iterates the server's installed mods, looks
    /// each one up by exact name on Nexus and on 7daystodiemods.com, compares
    /// versions, and returns a status row per installed mod. Stateless; no
    /// persistence — admin presses the button, gets a fresh result set, UI
    /// renders the badges.
    ///
    /// Match strategy: case-insensitive exact-name match (plus, on
    /// 7daystodiemods.com, the listing slug against the slugified name). We
    /// deliberately don't fuzzy-match because false positives (different mod
    /// with a similar name) would be worse than a "no match" outcome — the admin
    /// would chase the wrong update. A mod listed on both sites gets whichever
    /// answer is most useful: an available update wins, then a clear "up to date".
    /// </summary>
    public class ModUpdateService
    {
        private readonly ModManagerService _modManager;
        private readonly NexusModDiscoveryService _nexus;
        private readonly SevenDaysModsService _sevenDaysMods;
        private const string NexusGameSlug = "7daystodie";

        public ModUpdateService(ModManagerService modManager, NexusModDiscoveryService nexus, SevenDaysModsService sevenDaysMods)
        {
            _modManager = modManager;
            _nexus = nexus;
            _sevenDaysMods = sevenDaysMods;
        }

        public List<ModUpdateCheckResult> CheckAll()
        {
            var results = new List<ModUpdateCheckResult>();
            var installed = _modManager.GetMods();

            foreach (var mod in installed)
            {
                // KitsuneCommand itself is "protected" — skip silently. Admins
                // update KC by replacing the deployed mod folder, not via Nexus.
                if (mod.IsProtected)
                {
                    results.Add(new ModUpdateCheckResult
                    {
                        FolderName = mod.FolderName,
                        Status = "skipped",
                        InstalledVersion = mod.Version,
                    });
                    continue;
                }

                // Use display name for the search (it's what Nexus indexes), fall
                // back to folder name if display is empty.
                var searchName = !string.IsNullOrWhiteSpace(mod.DisplayName)
                    ? mod.DisplayName
                    : mod.FolderName;

                // The folder name is the fallback — useful for mods whose ModInfo
                // display name diverged from the listing name.
                var tryFolder = !string.Equals(mod.FolderName, searchName, StringComparison.OrdinalIgnoreCase);

                var candidates = new List<ModUpdateCheckResult>();

                var nexusMatch = FindExactMatch(searchName) ?? (tryFolder ? FindExactMatch(mod.FolderName) : null);
                if (nexusMatch != null)
                {
                    candidates.Add(new ModUpdateCheckResult
                    {
                        FolderName = mod.FolderName,
                        Status = ClassifyVersion(mod.Version, nexusMatch.Version),
                        InstalledVersion = mod.Version,
                        LatestVersion = nexusMatch.Version,
                        Source = "nexus",
                        Url = BuildNexusUrl(nexusMatch.ModId),
                        NexusModId = nexusMatch.ModId,
                        NexusUrl = BuildNexusUrl(nexusMatch.ModId),
                        MatchedName = nexusMatch.Name,
                    });
                }

                var siteMatch = _sevenDaysMods.FindExact(searchName) ?? (tryFolder ? _sevenDaysMods.FindExact(mod.FolderName) : null);
                if (siteMatch != null)
                {
                    candidates.Add(new ModUpdateCheckResult
                    {
                        FolderName = mod.FolderName,
                        Status = ClassifyVersion(mod.Version, siteMatch.Version),
                        InstalledVersion = mod.Version,
                        LatestVersion = siteMatch.Version,
                        Source = "7daystodiemods",
                        Url = siteMatch.Url,
                        MatchedName = siteMatch.Title,
                    });
                }

                results.Add(PickBest(candidates) ?? new ModUpdateCheckResult
                {
                    FolderName = mod.FolderName,
                    Status = "no_match",
                    InstalledVersion = mod.Version,
                });
            }

            return results;
        }

        /// <summary>
        /// One row from a mod's per-source results: an available update first
        /// (newest version if both sources have one), then "up to date", then
        /// "version differs". Null when there's no candidate.
        /// </summary>
        public static ModUpdateCheckResult PickBest(IEnumerable<ModUpdateCheckResult> candidates)
        {
            int Rank(string status) => status == "update_available" ? 0 : status == "up_to_date" ? 1 : 2;
            return candidates
                .OrderBy(c => Rank(c.Status))
                .ThenByDescending(c => TryParseVersion(c.LatestVersion, out var v) ? v : new Version(0, 0))
                .FirstOrDefault();
        }

        /// <summary>
        /// Search Nexus by exact name (case-insensitive). Returns null if no
        /// exact match is found. We don't accept fuzzy matches because the
        /// downstream version comparison would be meaningless against a
        /// different mod.
        /// </summary>
        private NexusModInfo FindExactMatch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // Page size 20 is plenty — exact matches surface near the top.
            var search = _nexus.SearchMods(name, "endorsements", 0, 20);
            if (search?.Mods == null) return null;
            return search.Mods.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Compares two version strings. Tries System.Version parse first
        /// (handles 1.0.0, 2.1.3.4, etc.) — falls back to string equality
        /// so that any difference still surfaces, even for non-numeric
        /// version schemes like "v2-beta".
        /// </summary>
        public static string ClassifyVersion(string installed, string latest)
        {
            if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(latest))
                return "version_differs";

            if (string.Equals(installed.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase))
                return "up_to_date";

            if (TryParseVersion(installed, out var iv) && TryParseVersion(latest, out var lv))
            {
                if (lv > iv) return "update_available";
                if (lv < iv) return "up_to_date"; // installed is newer than what Nexus knows — treat as fine
                return "up_to_date";
            }

            // Non-parseable scheme on either side — admin investigates manually.
            return "version_differs";
        }

        private static bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            // Strip a leading "v" / "V" prefix the way Nexus and authors often write it.
            var trimmed = raw.Trim().TrimStart('v', 'V').Trim();
            // Drop anything after the first non-version character (e.g. "-beta", "+build").
            var cutoff = trimmed.IndexOfAny(new[] { '-', '+', ' ' });
            if (cutoff > 0) trimmed = trimmed.Substring(0, cutoff);
            return Version.TryParse(trimmed, out version);
        }

        private static string BuildNexusUrl(int modId) =>
            $"https://www.nexusmods.com/{NexusGameSlug}/mods/{modId}";
    }
}
