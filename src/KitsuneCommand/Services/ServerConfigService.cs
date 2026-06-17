using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using KitsuneCommand.Core;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Reads and writes the 7 Days to Die serverconfig.xml file.
    /// Preserves XML structure, comments, and formatting on write.
    /// </summary>
    public class ServerConfigService
    {
        private string _configPath;

        /// <summary>
        /// Locates the serverconfig.xml file. Searches common locations.
        /// </summary>
        public virtual string GetConfigPath()
        {
            if (_configPath != null && File.Exists(_configPath))
                return _configPath;

            // Derive game root from mod path: Mods/KitsuneCommand -> ../../
            var gameDir = Path.GetFullPath(Path.Combine(ModEntry.ModPath, "..", ".."));

            var candidates = new List<string>
            {
                Path.Combine(gameDir, "serverconfig.xml"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serverconfig.xml"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "serverconfig.xml"),
            };

            // Also try via GamePrefs if available
            try
            {
                var userDataDir = GameIO.GetUserGameDataDir();
                if (!string.IsNullOrEmpty(userDataDir))
                    candidates.Add(Path.Combine(userDataDir, "serverconfig.xml"));
            }
            catch { /* GameIO may not be available yet */ }

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    _configPath = Path.GetFullPath(path);
                    return _configPath;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads all properties from serverconfig.xml as key-value pairs.
        /// </summary>
        public Dictionary<string, string> ReadConfig()
        {
            var path = GetConfigPath();
            if (path == null)
                throw new FileNotFoundException("serverconfig.xml not found.");

            var doc = XDocument.Load(path);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in doc.Descendants("property"))
            {
                var name = prop.Attribute("name")?.Value;
                var value = prop.Attribute("value")?.Value;
                if (name != null)
                {
                    result[name] = value ?? "";
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the raw XML content of the config file.
        /// </summary>
        public string ReadRawXml()
        {
            var path = GetConfigPath();
            if (path == null)
                throw new FileNotFoundException("serverconfig.xml not found.");

            return File.ReadAllText(path);
        }

        /// <summary>
        /// Updates properties in the config file, preserving structure and comments.
        /// Writes to BOTH serverconfig.xml AND serverconfig.xml.bak. The .bak copy is the
        /// "sticky golden copy" that kitsune-pre-start.sh restores on every server start
        /// (to survive steamcmd updates overwriting the file with TFP defaults). Keeping
        /// them in sync means the UI's saves actually persist across restarts.
        /// </summary>
        public void SaveConfig(Dictionary<string, string> properties)
        {
            var path = GetConfigPath();
            if (path == null)
                throw new FileNotFoundException("serverconfig.xml not found.");

            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);

            foreach (var kvp in properties)
            {
                var existing = doc.Descendants("property")
                    .FirstOrDefault(p => string.Equals(
                        p.Attribute("name")?.Value, kvp.Key,
                        StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.SetAttributeValue("value", kvp.Value);
                }
                else
                {
                    // Insert new property before the closing </ServerSettings> tag
                    var root = doc.Root;
                    if (root != null)
                    {
                        root.Add(new XElement("property",
                            new XAttribute("name", kvp.Key),
                            new XAttribute("value", kvp.Value)));
                    }
                }
            }

            doc.Save(path);
            // Update the sticky .bak so pre-start restore keeps our changes
            File.Copy(path, path + ".bak", true);
        }

        /// <summary>
        /// Saves raw XML content to the config file.
        /// Creates a .bak backup before writing.
        /// </summary>
        public void SaveRawXml(string xmlContent)
        {
            var path = GetConfigPath();
            if (path == null)
                throw new FileNotFoundException("serverconfig.xml not found.");

            // Validate XML before saving
            XDocument.Parse(xmlContent); // Throws if invalid

            File.WriteAllText(path, xmlContent);
            // Update the sticky .bak so pre-start restore keeps our changes
            File.Copy(path, path + ".bak", true);
        }

        /// <summary>
        /// Lists available world names from the Data/Worlds directory.
        /// </summary>
        public List<string> GetAvailableWorlds()
        {
            var worlds = new List<string> { "Navezgane" };
            var gameDir = Path.GetFullPath(Path.Combine(ModEntry.ModPath, "..", ".."));

            try
            {
                var worldsDir = Path.Combine(gameDir, "Data", "Worlds");
                if (Directory.Exists(worldsDir))
                {
                    foreach (var dir in Directory.GetDirectories(worldsDir))
                    {
                        var name = Path.GetFileName(dir);
                        if (!worlds.Contains(name, StringComparer.OrdinalIgnoreCase))
                            worlds.Add(name);
                    }
                }

                // Also check for generated worlds in save directory
                try
                {
                    var saveDir = GameIO.GetSaveGameDir();
                    if (!string.IsNullOrEmpty(saveDir) && Directory.Exists(saveDir))
                    {
                        foreach (var dir in Directory.GetDirectories(Path.GetDirectoryName(saveDir) ?? saveDir))
                        {
                            var name = Path.GetFileName(dir);
                            if (!worlds.Contains(name, StringComparer.OrdinalIgnoreCase))
                                worlds.Add(name);
                        }
                    }
                }
                catch { /* GameIO may not be ready */ }
            }
            catch { /* Fallback to just Navezgane */ }

            return worlds;
        }

        /// <summary>
        /// Gets the field definitions for the config editor UI.
        /// </summary>
        public List<ConfigFieldGroup> GetFieldDefinitions()
        {
            return ServerConfigFieldDefinitions.GetGroups();
        }

        /// <summary>
        /// True when serverconfig.xml still carries 3.0-deprecated sandbox-governed
        /// properties as live elements — i.e. a migration would do something. Lets the UI
        /// offer "Migrate to 3.0" only when there's actually something to clean up (and not
        /// after it's already been done). Returns false if the file can't be read.
        /// </summary>
        public bool NeedsMigrationTo30()
        {
            try
            {
                var path = GetConfigPath();
                if (path == null) return false;
                var doc = XDocument.Load(path);
                var governed = new HashSet<string>(
                    ServerConfigFieldDefinitions.GetSandboxGovernedKeys(), StringComparer.OrdinalIgnoreCase);
                return doc.Descendants("property")
                    .Any(p => governed.Contains(p.Attribute("name")?.Value ?? ""));
            }
            catch { return false; }
        }

        /// <summary>
        /// Migrate serverconfig.xml to the 7D2D 3.0 ("Dead Hot Summer") shape.
        ///
        /// 3.0 moved a set of world/gameplay settings out of serverconfig.xml and into the
        /// in-game Sandbox, governed by a single SandboxCode property; on a 3.0 server the
        /// old individual properties are IGNORED. This:
        ///   1. comments out (does NOT delete) each sandbox-governed property, preserving its
        ///      value so a downgrade or reference stays possible;
        ///   2. ensures a SandboxCode property exists (empty — pasted in via the editor);
        ///   3. leaves the "survivor" properties and everything KC doesn't model untouched.
        ///
        /// Safety: takes a timestamped backup BEFORE writing, is idempotent (a second run is
        /// a no-op), and refreshes the sticky .bak so the pre-start restore keeps the result.
        /// The caller gates this on a real 3.0 install (ServerConfigService.GameSupportsSandboxCode);
        /// the method itself is pure file work so it stays unit-testable off a live game.
        /// </summary>
        public ConfigMigrationResult MigrateConfigTo30()
        {
            var result = new ConfigMigrationResult();
            var path = GetConfigPath();
            if (path == null)
                throw new FileNotFoundException("serverconfig.xml not found.");

            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null)
                throw new InvalidOperationException("serverconfig.xml has no root element.");

            var governed = new HashSet<string>(
                ServerConfigFieldDefinitions.GetSandboxGovernedKeys(), StringComparer.OrdinalIgnoreCase);

            // 1. Neutralize the sandbox-governed properties by replacing each with an XML
            //    comment that preserves its old name/value (never hard-delete operator data).
            var toNeutralize = root.Descendants("property")
                .Where(p => governed.Contains(p.Attribute("name")?.Value ?? ""))
                .ToList();
            foreach (var prop in toNeutralize)
            {
                var name = prop.Attribute("name")?.Value ?? "";
                var value = prop.Attribute("value")?.Value ?? "";
                // XML comments can't contain "--"; swap any out so the document stays valid.
                var body = $" property name=\"{name}\" value=\"{value}\" — moved to the in-game Sandbox in 3.0 (governed by SandboxCode) "
                    .Replace("--", "—");
                prop.ReplaceWith(new XComment(body));
                result.Neutralized.Add(name);
            }

            // 2. Ensure SandboxCode exists for the operator to paste their code into.
            var hasSandbox = root.Descendants("property")
                .Any(p => string.Equals(p.Attribute("name")?.Value, "SandboxCode", StringComparison.OrdinalIgnoreCase));
            if (!hasSandbox)
            {
                root.Add(new XElement("property",
                    new XAttribute("name", "SandboxCode"),
                    new XAttribute("value", "")));
                result.AddedSandboxCode = true;
            }

            result.Changed = result.Neutralized.Count > 0 || result.AddedSandboxCode;
            if (!result.Changed)
                return result; // already 3.0-shaped — idempotent no-op, no backup churn

            // 3. Timestamped backup BEFORE writing (distinct from the sticky .bak).
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            result.BackupPath = path + $".pre30-{stamp}.bak";
            File.Copy(path, result.BackupPath, true);

            doc.Save(path);
            // Refresh the sticky .bak so the pre-start restore keeps the migrated file.
            File.Copy(path, path + ".bak", true);
            return result;
        }

        /// <summary>
        /// True when the running game is 7D2D 3.0+ (supports the SandboxCode system),
        /// detected by reflecting EnumGamePrefs for a "SandboxCode" member. Done via
        /// Enum.GetNames rather than a direct EnumGamePrefs.SandboxCode reference so it
        /// compiles against the 2.x reference assemblies yet returns true at runtime on a
        /// 3.0 server. The editor uses this to decide whether to show the SandboxCode field
        /// (it should appear on any 3.0 server, even before a code has been pasted in).
        /// </summary>
        public bool GameSupportsSandboxCode()
        {
            try { return System.Array.IndexOf(System.Enum.GetNames(typeof(EnumGamePrefs)), "SandboxCode") >= 0; }
            catch { return false; }
        }
    }

    /// <summary>Outcome of <see cref="ServerConfigService.MigrateConfigTo30"/>.</summary>
    public class ConfigMigrationResult
    {
        /// <summary>True if the file was actually rewritten (false = already 3.0-shaped).</summary>
        public bool Changed { get; set; }
        /// <summary>True if a SandboxCode property was added (it was missing).</summary>
        public bool AddedSandboxCode { get; set; }
        /// <summary>Names of the sandbox-governed properties that were commented out.</summary>
        public List<string> Neutralized { get; set; } = new List<string>();
        /// <summary>Path of the timestamped backup taken before writing, or null if no change.</summary>
        public string BackupPath { get; set; }
    }
}
