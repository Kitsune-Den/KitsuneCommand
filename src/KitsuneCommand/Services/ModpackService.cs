using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Single source of truth for the server's player-facing modpack: which
    /// installed mods are bundled, the published name + version, and the
    /// on-disk zip used by the public download endpoint.
    ///
    /// Storage layout:
    ///   &lt;game-root&gt;/KitsuneModpacks/      ← zip output
    ///   &lt;game-root&gt;/Mods/&lt;modName&gt;/      ← source per bundled mod
    ///
    /// "Latest version only" semantics: at most one row in the modpack table.
    /// Edits update in place. Publishing a new version overwrites the previous
    /// zip on disk.
    /// </summary>
    public class ModpackService
    {
        private readonly IModpackRepository _repo;
        private readonly ModManagerService _modManager;

        public ModpackService(IModpackRepository repo, ModManagerService modManager)
        {
            _repo = repo;
            _modManager = modManager;
        }

        /// <summary>Path to the directory that holds built modpack zips.</summary>
        public string GetModpackDir()
        {
            // Game root is the parent of Mods/. ModManagerService.GetModsPath
            // already does the rooted-path resolution dance — reuse it.
            var modsPath = _modManager.GetModsPath();
            var gameRoot = Path.GetDirectoryName(modsPath);
            var dir = Path.Combine(gameRoot ?? Directory.GetCurrentDirectory(), "KitsuneModpacks");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Current modpack record (any status), or null if none configured.</summary>
        public Modpack Get() => _repo.GetCurrent();

        /// <summary>
        /// Save or update the draft. If a record already exists, this updates
        /// it in place and flips its status back to 'draft' so the published
        /// zip on disk doesn't drift out of sync with the (now-changed)
        /// configuration. Caller should run BuildZip + Publish to re-publish.
        /// </summary>
        public Modpack SaveDraft(string name, string version, List<string> modList, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Modpack name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Modpack version is required.", nameof(version));
            if (modList == null) modList = new List<string>();

            // Validate every requested mod actually exists in the Mods/ dir.
            // Better to fail loudly here than silently produce an empty zip
            // entry at build time.
            var installed = _modManager.GetMods().Select(m => m.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = modList.Where(m => !installed.Contains(m)).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot include mods not installed on the server: {string.Join(", ", missing)}.");
            }

            var existing = _repo.GetCurrent();
            var record = existing ?? new Modpack { CreatedAt = DateTime.UtcNow.ToString("o") };
            record.Name = name.Trim();
            record.Version = version.Trim();
            record.ModList = JsonConvert.SerializeObject(modList);
            record.ModCount = modList.Count;
            record.Description = description;

            // Editing config invalidates the previously-built zip. Force a
            // re-build before re-publish. We don't delete the old zip here
            // (BuildZip handles overwrite-on-build), so an existing public
            // download keeps working until the admin explicitly rebuilds.
            // Status flips to 'draft' if we're touching previously-published
            // settings; archived stays archived.
            if (record.Status != "archived")
                record.Status = "draft";

            return _repo.Upsert(record);
        }

        /// <summary>
        /// Build the on-disk zip from the current mod selection. Overwrites
        /// any previous zip for this modpack record. Updates the record's
        /// filename + size_bytes on success.
        /// </summary>
        public Modpack BuildZip()
        {
            var record = _repo.GetCurrent()
                ?? throw new InvalidOperationException("No modpack configured yet. Save a draft first.");

            var modNames = JsonConvert.DeserializeObject<List<string>>(record.ModList) ?? new List<string>();
            if (modNames.Count == 0)
                throw new InvalidOperationException("Modpack has no mods selected. Pick at least one before building.");

            var modsRoot = _modManager.GetModsPath();
            var outputDir = GetModpackDir();
            var safeName = SanitizeForFilename($"{record.Name}-{record.Version}");
            var filename = $"{safeName}.zip";
            var outputPath = Path.Combine(outputDir, filename);

            // If the previously-built zip used a different filename (admin
            // changed name or version), clean it up so we don't accumulate
            // orphan files in the modpacks dir.
            if (!string.IsNullOrEmpty(record.Filename) && record.Filename != filename)
            {
                var oldPath = Path.Combine(outputDir, record.Filename);
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { /* best-effort */ }
            }

            // Build into a temp file first, then move into place. Avoids a
            // partial-write window where a public downloader could grab a
            // corrupt half-zip.
            var tempPath = outputPath + ".building";
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* fall through */ }

            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var modName in modNames)
                {
                    var modDir = Path.Combine(modsRoot, modName);
                    if (!Directory.Exists(modDir))
                    {
                        // Caller previously validated against installed mods,
                        // but a mod could have been removed between draft-save
                        // and build. Skip silently with no entry rather than
                        // poisoning the zip.
                        continue;
                    }
                    AddDirectoryToZip(zip, modDir, modName);
                }
            }

            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* best-effort */ }
            }
            File.Move(tempPath, outputPath);

            var fi = new FileInfo(outputPath);
            record.Filename = filename;
            record.SizeBytes = fi.Length;
            return _repo.Upsert(record);
        }

        /// <summary>Flips status to 'published'. Caller should have run BuildZip first.</summary>
        public Modpack Publish()
        {
            var record = _repo.GetCurrent()
                ?? throw new InvalidOperationException("No modpack to publish.");
            if (string.IsNullOrEmpty(record.Filename))
                throw new InvalidOperationException("Modpack zip hasn't been built yet. Build first, then publish.");
            record.Status = "published";
            return _repo.Upsert(record);
        }

        /// <summary>
        /// Flips status to 'archived'. Public download stops; the zip stays on
        /// disk in case admin wants to re-publish or download a copy.
        /// </summary>
        public Modpack Archive()
        {
            var record = _repo.GetCurrent()
                ?? throw new InvalidOperationException("No modpack to archive.");
            record.Status = "archived";
            return _repo.Upsert(record);
        }

        /// <summary>
        /// Flips status to 'draft'. Useful for reviving an archived pack
        /// without losing its history of edits.
        /// </summary>
        public Modpack Unarchive()
        {
            var record = _repo.GetCurrent()
                ?? throw new InvalidOperationException("No modpack to unarchive.");
            record.Status = "draft";
            return _repo.Upsert(record);
        }

        /// <summary>Wipe everything: the DB row and the zip file.</summary>
        public void Delete()
        {
            var record = _repo.GetCurrent();
            if (record == null) return;

            if (!string.IsNullOrEmpty(record.Filename))
            {
                var path = Path.Combine(GetModpackDir(), record.Filename);
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
            _repo.Delete(record.Id);
        }

        /// <summary>
        /// Opens the published zip for streaming. Returns null if no pack is
        /// published or the file is missing. Caller is responsible for
        /// disposing the stream and bumping the download counter.
        /// </summary>
        public (Modpack Record, FileStream Stream) OpenPublishedForDownload()
        {
            var record = _repo.GetPublished();
            if (record == null || string.IsNullOrEmpty(record.Filename))
                return (null, null);

            var path = Path.Combine(GetModpackDir(), record.Filename);
            if (!File.Exists(path))
                return (record, null);

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (record, stream);
        }

        public void IncrementDownloadCount(int id) => _repo.IncrementDownloadCount(id);

        // ─── helpers ──────────────────────────────────────────────────────

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryRoot)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                // ZIP entries use forward slashes per the spec — even when
                // built on Windows. Picky archivers (and 7-Zip on Linux) will
                // misrender Windows-style backslashes as part of the filename.
                var entryName = entryRoot + "/" + rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }

        private static readonly Regex UnsafeFilenameChars =
            new Regex(@"[^A-Za-z0-9._\-]+", RegexOptions.Compiled);

        private static string SanitizeForFilename(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "modpack";
            var clean = UnsafeFilenameChars.Replace(s, "_").Trim('_', '.');
            return string.IsNullOrEmpty(clean) ? "modpack" : clean;
        }
    }
}
