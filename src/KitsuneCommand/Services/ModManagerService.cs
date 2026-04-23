using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using KitsuneCommand.Core;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Manages server mods in the Mods/ directory. Supports listing, uploading (ZIP),
    /// deleting, and enabling/disabling mods.
    /// </summary>
    public class ModManagerService
    {
        private string _modsPath;

        /// <summary>
        /// Gets the path to the Mods directory.
        /// </summary>
        public string GetModsPath()
        {
            if (_modsPath != null && Directory.Exists(_modsPath))
                return _modsPath;

            // Derive game root from mod path: Mods/KitsuneCommand -> ../../
            var gameDir = Path.GetFullPath(Path.Combine(ModEntry.ModPath, "..", ".."));

            var candidates = new[]
            {
                Path.Combine(gameDir, "Mods"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Mods"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                {
                    _modsPath = Path.GetFullPath(path);
                    return _modsPath;
                }
            }

            // Create default location under game root
            _modsPath = Path.GetFullPath(Path.Combine(gameDir, "Mods"));
            Directory.CreateDirectory(_modsPath);
            return _modsPath;
        }

        /// <summary>
        /// Lists all installed mods with metadata from ModInfo.xml.
        /// </summary>
        public List<ModInfo> GetMods()
        {
            var modsPath = GetModsPath();
            var mods = new List<ModInfo>();

            if (!Directory.Exists(modsPath))
                return mods;

            foreach (var dir in Directory.GetDirectories(modsPath))
            {
                var folderName = Path.GetFileName(dir);
                var isDisabled = folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var mod = new ModInfo
                {
                    FolderName = folderName,
                    DisplayName = isDisabled ? folderName.Substring(0, folderName.Length - 9) : folderName,
                    IsEnabled = !isDisabled,
                    FolderSize = GetDirectorySize(dir),
                    IsProtected = folderName.Equals("KitsuneCommand", StringComparison.OrdinalIgnoreCase),
                };

                // Try to read ModInfo.xml (case-insensitive lookup — on Linux,
                // File.Exists is case-sensitive and would miss "modinfo.xml" or
                // "MODINFO.xml" variants that some community mods ship with).
                var modInfoPath = Directory.GetFiles(dir, "*.xml", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(p => Path.GetFileName(p).Equals("ModInfo.xml", StringComparison.OrdinalIgnoreCase));
                if (modInfoPath != null)
                {
                    try
                    {
                        var doc = XDocument.Load(modInfoPath);
                        mod.DisplayName = doc.Descendants("Name").FirstOrDefault()?.Attribute("value")?.Value ?? mod.DisplayName;
                        mod.Version = doc.Descendants("Version").FirstOrDefault()?.Attribute("value")?.Value;
                        mod.Author = doc.Descendants("Author").FirstOrDefault()?.Attribute("value")?.Value;
                        mod.Description = doc.Descendants("Description").FirstOrDefault()?.Attribute("value")?.Value;
                        mod.Website = doc.Descendants("Website").FirstOrDefault()?.Attribute("value")?.Value;
                    }
                    catch { /* Ignore malformed ModInfo.xml */ }
                }

                mods.Add(mod);
            }

            return mods.OrderBy(m => m.DisplayName).ToList();
        }

        /// <summary>
        /// Uploads and extracts a ZIP file as one or more mods. Smart about
        /// community/Nexus zip shapes:
        ///
        /// - Walks the extracted tree for every <c>ModInfo.xml</c> and treats each
        ///   containing folder as a separate mod root. Handles a single mod at the
        ///   top level, a mod wrapped inside an outer folder, AND mod packs.
        /// - Uses the <c>&lt;Name value="..."/&gt;</c> attribute from ModInfo.xml as
        ///   the canonical folder name — strips out Nexus-style suffixes like
        ///   <c>-10022-1-1775089368</c> automatically since they don't appear inside
        ///   the actual ModInfo.
        /// - If no ModInfo.xml is found anywhere, falls back to the old behavior
        ///   (extract whatever's in there to a folder named after the zip) and
        ///   surfaces a warning so the operator knows the metadata won't render.
        /// </summary>
        public ModUploadResult UploadMod(Stream zipStream, string fileName)
        {
            var modsPath = GetModsPath();
            var result = new ModUploadResult { SourceFileName = fileName };

            var tempDir = Path.Combine(Path.GetTempPath(), "kc_mod_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                ExtractZipSafely(zipStream, tempDir);

                // Walk for all ModInfo.xml — each containing folder is a mod root.
                // Case-insensitive on purpose: Directory.GetFiles with a pattern is
                // case-sensitive on Linux (Mono), so "ModInfo.xml" would miss a mod
                // that ships "modinfo.xml" or "MODINFO.xml". Iterate + filter manually.
                var modInfoFiles = Directory.GetFiles(tempDir, "*.xml", SearchOption.AllDirectories)
                    .Where(p => Path.GetFileName(p).Equals("ModInfo.xml", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (modInfoFiles.Length == 0)
                {
                    // No metadata anywhere. InstallFolderFromFilename tries to be
                    // smart about folder shape, and InstallSingleFolder auto-generates
                    // a minimal ModInfo.xml so the mod is actually loadable by 7D2D.
                    // No top-level warning needed — individual install steps will
                    // note what they did.
                    var installedFallback = InstallFolderFromFilename(tempDir, fileName, modsPath, result);
                    result.InstalledMods.AddRange(installedFallback);
                    return result;
                }

                foreach (var modInfoPath in modInfoFiles)
                {
                    var sourceDir = Path.GetDirectoryName(modInfoPath);
                    if (string.IsNullOrEmpty(sourceDir)) continue;

                    var canonicalName = ReadModName(modInfoPath)
                                        ?? Path.GetFileName(sourceDir);
                    canonicalName = SanitizeFolderName(canonicalName);

                    if (string.IsNullOrWhiteSpace(canonicalName))
                    {
                        result.Warnings.Add($"Could not derive a folder name for {modInfoPath} — skipping.");
                        continue;
                    }

                    // Safety: never let an upload clobber KitsuneCommand itself.
                    if (canonicalName.Equals("KitsuneCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add(
                            "This zip contains a mod named 'KitsuneCommand' which would overwrite the live panel. " +
                            "Skipped for safety — upload a specifically-named version if this was intentional.");
                        continue;
                    }

                    var destDir = Path.Combine(modsPath, canonicalName);
                    var replaced = Directory.Exists(destDir);
                    if (replaced) Directory.Delete(destDir, true);

                    CopyDirectory(sourceDir, destDir);

                    var installed = GetMods()
                        .FirstOrDefault(m => m.FolderName.Equals(canonicalName, StringComparison.OrdinalIgnoreCase))
                        ?? new ModInfo { FolderName = canonicalName, DisplayName = canonicalName, IsEnabled = true };

                    result.InstalledMods.Add(installed);

                    if (replaced)
                        result.Warnings.Add($"Replaced existing mod '{canonicalName}' with the uploaded version.");
                }

                return result;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>
        /// Fallback installer when no ModInfo.xml is detected. Tries to be smart:
        ///
        ///  - If the zip wraps content in a single top-level folder, drill into it
        ///    first (Nexus-style zip wrappers are common).
        ///  - If the resulting working directory contains only subdirectories
        ///    (no files at root), treat each subdirectory as a separate mod.
        ///    This handles mod-pack zips that bundle several mods together even
        ///    when the individual mods lack ModInfo.xml.
        ///  - Otherwise install as a single mod named after the folder/zip.
        /// </summary>
        private List<ModInfo> InstallFolderFromFilename(string tempDir, string fileName, string modsPath, ModUploadResult result)
        {
            var installed = new List<ModInfo>();

            // Drill into a single wrapping folder if there is one.
            var topItems = Directory.GetFileSystemEntries(tempDir);
            string workingDir;
            if (topItems.Length == 1 && Directory.Exists(topItems[0]))
            {
                workingDir = topItems[0];
            }
            else
            {
                workingDir = tempDir;
            }

            var inner = Directory.GetFileSystemEntries(workingDir);
            var allAreDirs = inner.Length > 0 && inner.All(Directory.Exists);

            if (allAreDirs && inner.Length > 1)
            {
                // Looks like a pack: several folders, no files at this level.
                // Install each sub-folder as its own mod.
                foreach (var subDir in inner)
                {
                    var mod = InstallSingleFolder(subDir, Path.GetFileName(subDir), modsPath, result);
                    if (mod != null) installed.Add(mod);
                }

                if (installed.Count > 0)
                {
                    result.Warnings.Add(
                        $"No ModInfo.xml anywhere — installed {installed.Count} sub-folders as separate mods. " +
                        "Their metadata won't render until each includes a ModInfo.xml.");
                }
                return installed;
            }

            // Single mod — use the working folder name (or zip filename if we're at tempDir)
            var modFolderName = workingDir == tempDir
                ? Path.GetFileNameWithoutExtension(fileName)
                : Path.GetFileName(workingDir);

            var single = InstallSingleFolder(workingDir, modFolderName, modsPath, result);
            if (single != null) installed.Add(single);
            return installed;
        }

        /// <summary>
        /// Helper: copy a folder to <c>Mods/&lt;sanitizedName&gt;</c>, with the usual
        /// safety checks (no KitsuneCommand clobbering, replace-with-warning).
        /// If the folder has no ModInfo.xml, auto-generates a minimal one so 7D2D
        /// actually loads the mod at server boot (the engine requires the file).
        /// </summary>
        private ModInfo InstallSingleFolder(string sourceDir, string rawName, string modsPath, ModUploadResult result)
        {
            var modFolderName = SanitizeFolderName(rawName);

            if (string.IsNullOrWhiteSpace(modFolderName)) return null;

            if (modFolderName.Equals("KitsuneCommand", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"Skipped '{rawName}' — would overwrite KitsuneCommand.");
                return null;
            }

            var destDir = Path.Combine(modsPath, modFolderName);
            var replaced = Directory.Exists(destDir);
            if (replaced) Directory.Delete(destDir, true);

            CopyDirectory(sourceDir, destDir);

            // Generate a minimal ModInfo.xml if none exists. 7D2D requires the file
            // for the mod loader to see the folder as a mod — without it, files sit
            // on disk but the server skips them on boot. Use the folder name as both
            // Name and DisplayName; operator can fill in author/version/etc later.
            var modInfoPath = Path.Combine(destDir, "ModInfo.xml");
            if (!File.Exists(modInfoPath))
            {
                WriteMinimalModInfo(modInfoPath, modFolderName);
                result.Warnings.Add(
                    $"'{modFolderName}' shipped without a ModInfo.xml — generated a minimal one so 7D2D will load it. " +
                    $"Edit Mods/{modFolderName}/ModInfo.xml to add version and author.");
            }

            if (replaced)
                result.Warnings.Add($"Replaced existing mod '{modFolderName}'.");

            return GetMods()
                .FirstOrDefault(m => m.FolderName.Equals(modFolderName, StringComparison.OrdinalIgnoreCase))
                ?? new ModInfo { FolderName = modFolderName, DisplayName = modFolderName, IsEnabled = true };
        }

        /// <summary>
        /// Writes a minimal ModInfo.xml to the given path. Uses the folder name
        /// for both Name and DisplayName; everything else left blank so the
        /// operator can fill in.
        /// </summary>
        private static void WriteMinimalModInfo(string path, string modName)
        {
            var displayName = PrettifyModName(modName);
            var xml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<xml>\n" +
                $"    <Name value=\"{System.Security.SecurityElement.Escape(modName)}\" />\n" +
                $"    <DisplayName value=\"{System.Security.SecurityElement.Escape(displayName)}\" />\n" +
                "    <Version value=\"\" />\n" +
                "    <Description value=\"\" />\n" +
                "    <Author value=\"\" />\n" +
                "    <Website value=\"\" />\n" +
                "</xml>\n";
            File.WriteAllText(path, xml);
        }

        /// <summary>
        /// Turns a raw folder name like "KitsuneKitchen V1.1.0" or "SomeMod_v2"
        /// into a more readable display name. Conservative — just spacing and
        /// common cruft-trimming, not aggressive renaming.
        /// </summary>
        private static string PrettifyModName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            // Insert space between camelCase transitions
            var spaced = System.Text.RegularExpressions.Regex.Replace(raw, @"([a-z])([A-Z])", "$1 $2");
            return spaced.Trim();
        }

        /// <summary>
        /// Reads the <c>&lt;Name value="..."/&gt;</c> attribute out of a ModInfo.xml.
        /// Returns null if unreadable or empty.
        /// </summary>
        private static string ReadModName(string modInfoPath)
        {
            try
            {
                var doc = XDocument.Load(modInfoPath);
                var name = doc.Descendants("Name").FirstOrDefault()?.Attribute("value")?.Value;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch { return null; }
        }

        /// <summary>
        /// Makes a string safe to use as a directory name and strips
        /// Nexus-style filename cruft (trailing -digits-digits-digits pattern).
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // Strip any path separators and illegal characters
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            // Nexus filenames look like "ModName-<modId>-<version>-<timestamp>"
            // e.g. "KitsuneKitchen V1.1.0-10022-1-1775089368".
            // When we fell through to the filename fallback, strip the trailing
            // -digits-digits-digits pattern to get back to a human name.
            var trimmed = System.Text.RegularExpressions.Regex.Replace(
                name, @"-\d+-\d+-\d+$", "");

            return trimmed.Trim();
        }

        private static void ExtractZipSafely(Stream zipStream, string tempDir)
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    // Some zips use forward slashes (unix/cross-platform tooling) while
                    // others use backslashes (zipped on Windows without normalization,
                    // e.g. some Nexus community packagers). Normalize both so the
                    // extracted tree is actually a tree on every platform.
                    var entryPath = entry.FullName
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);

                    if (entryPath.Contains(".."))
                        throw new InvalidOperationException("ZIP contains path traversal entries.");

                    var destPath = Path.Combine(tempDir, entryPath);

                    // Also detect directory entries whose name terminates with either
                    // a forward slash OR a backslash (again: Windows-zipped zips).
                    var isDirectoryEntry = string.IsNullOrEmpty(entry.Name)
                        || entry.FullName.EndsWith("/")
                        || entry.FullName.EndsWith("\\");

                    if (isDirectoryEntry)
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a mod folder. Cannot delete KitsuneCommand itself.
        /// </summary>
        public void DeleteMod(string modName)
        {
            if (string.IsNullOrWhiteSpace(modName))
                throw new ArgumentException("Mod name is required.");

            // Safety: prevent deleting KitsuneCommand
            if (modName.Equals("KitsuneCommand", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot delete KitsuneCommand.");

            // Safety: prevent path traversal
            if (modName.Contains("..") || modName.Contains(Path.DirectorySeparatorChar.ToString())
                || modName.Contains(Path.AltDirectorySeparatorChar.ToString()))
                throw new InvalidOperationException("Invalid mod name.");

            var modPath = Path.Combine(GetModsPath(), modName);
            if (!Directory.Exists(modPath))
                throw new FileNotFoundException($"Mod '{modName}' not found.");

            Directory.Delete(modPath, true);
        }

        /// <summary>
        /// Toggles a mod enabled/disabled by renaming with .disabled suffix.
        /// </summary>
        public void ToggleMod(string modName)
        {
            if (string.IsNullOrWhiteSpace(modName))
                throw new ArgumentException("Mod name is required.");

            if (modName.Equals("KitsuneCommand", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot disable KitsuneCommand.");

            if (modName.Contains(".."))
                throw new InvalidOperationException("Invalid mod name.");

            var modsPath = GetModsPath();
            var modPath = Path.Combine(modsPath, modName);

            if (!Directory.Exists(modPath))
                throw new FileNotFoundException($"Mod '{modName}' not found.");

            if (modName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                // Enable: remove .disabled suffix
                var newName = modName.Substring(0, modName.Length - 9);
                Directory.Move(modPath, Path.Combine(modsPath, newName));
            }
            else
            {
                // Disable: add .disabled suffix
                Directory.Move(modPath, Path.Combine(modsPath, modName + ".disabled"));
            }
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    public class ModInfo
    {
        public string FolderName { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Website { get; set; }
        public long FolderSize { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsProtected { get; set; }
    }

    /// <summary>
    /// Result of a mod upload — supports zips that contain a single mod, a mod
    /// wrapped in an outer folder, or a mod pack with multiple ModInfo.xml files.
    /// Warnings surface recoverable issues (replaced existing, skipped protected,
    /// missing ModInfo) for the UI to display.
    /// </summary>
    public class ModUploadResult
    {
        public string SourceFileName { get; set; }
        public List<ModInfo> InstalledMods { get; set; } = new List<ModInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
