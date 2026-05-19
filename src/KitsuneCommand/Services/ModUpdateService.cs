using System;
using System.Collections.Generic;
using System.Linq;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Per-mod result of a "check Nexus for updates" sweep. The frontend renders
    /// these in the installed-mods table — Status drives the badge color and
    /// whether a Nexus link is shown.
    /// </summary>
    public class ModUpdateCheckResult
    {
        /// <summary>Local mod folder name (matches ModInfo.FolderName).</summary>
        public string FolderName { get; set; }

        /// <summary>"up_to_date" | "update_available" | "version_differs" | "no_match" | "skipped"</summary>
        public string Status { get; set; }

        public string InstalledVersion { get; set; }
        public string LatestVersion { get; set; }
        public int? NexusModId { get; set; }
        public string NexusUrl { get; set; }
        public string MatchedName { get; set; }
    }

    /// <summary>
    /// "Check for updates" backend. Iterates the server's installed mods, queries
    /// Nexus by exact name for each, compares versions, and returns a status row
    /// per installed mod. Stateless; no persistence — admin presses the button,
    /// gets a fresh result set, UI renders the badges.
    ///
    /// Match strategy: case-insensitive exact-name match against the GraphQL
    /// search results. We deliberately don't fuzzy-match because false positives
    /// (different mod with a similar name) would be worse than a "no match"
    /// outcome — the admin would chase the wrong update.
    /// </summary>
    public class ModUpdateService
    {
        private readonly ModManagerService _modManager;
        private readonly NexusModDiscoveryService _nexus;
        private const string NexusGameSlug = "7daystodie";

        public ModUpdateService(ModManagerService modManager, NexusModDiscoveryService nexus)
        {
            _modManager = modManager;
            _nexus = nexus;
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

                var match = FindExactMatch(searchName);
                if (match == null)
                {
                    // Try the folder name as a fallback — useful for mods whose
                    // ModInfo display name diverged from the Nexus listing name.
                    if (!string.Equals(mod.FolderName, searchName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = FindExactMatch(mod.FolderName);
                    }
                }

                if (match == null)
                {
                    results.Add(new ModUpdateCheckResult
                    {
                        FolderName = mod.FolderName,
                        Status = "no_match",
                        InstalledVersion = mod.Version,
                    });
                    continue;
                }

                var status = ClassifyVersion(mod.Version, match.Version);
                results.Add(new ModUpdateCheckResult
                {
                    FolderName = mod.FolderName,
                    Status = status,
                    InstalledVersion = mod.Version,
                    LatestVersion = match.Version,
                    NexusModId = match.ModId,
                    NexusUrl = BuildNexusUrl(match.ModId),
                    MatchedName = match.Name,
                });
            }

            return results;
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
        private static string ClassifyVersion(string installed, string latest)
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
