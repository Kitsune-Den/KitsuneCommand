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
        public string GetConfigPath()
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
    }
}
