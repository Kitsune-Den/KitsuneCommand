// End-to-end "Publish to PackRelay" logic. Stitches the crypto
// primitives + HTTP client into one flow that takes a curated mod
// folder list and produces a signed, published version on
// packrelay.cloud.
//
// Phases (each ticks through to the IProgress<PublishProgress>
// caller — Stage 5's UI streams these to the user):
//
//   1. Walking   — enumerate every file under the bundled mod folders.
//   2. Hashing   — sha256 each (streamed; never loads bytes to RAM).
//   3. Uploading — for each unique sha, BlobExists check then upload
//                  the missing ones. 8-way parallelism.
//   4. Signing   — build the canonical-JSON manifest, sign with
//                  the publisher's Ed25519 seed.
//   5. Posting   — POST signed manifest to /packs/<slug>/versions.
//   6. Done.
//
// Idempotency cornerstones:
//   - Already-uploaded blobs short-circuit at BlobExists. Re-running
//     a half-failed publish only pays the manifest POST again.
//   - 409 "duplicate_version" from PublishVersion is a SOFT success
//     (not a failure). The bytes are on the cloud, version is in
//     the catalog — we just rebuilt + uploaded again from a fresh
//     local copy. Caller sees AlreadyPublished=true.
//
// Concurrency: hashing + uploading run through a SemaphoreSlim with
// MaxParallel=8. Same shape as the launcher's install loop. Lower
// for slow links: a residential 50 Mbps connection saturates around
// 4 parallel 16-MB chunks; 8 leaves headroom for a gigabit pipe.
//
// File layout convention: manifest paths are POSIX-style forward
// slashes regardless of host OS. Windows back-slashes get normalized
// at walk time. The launcher's install loop joins paths with
// dest.join(file.path.replace(\\, /)) so either form works on its
// end, but the bytes that get signed are the forward-slash form —
// canonicalizing here keeps re-publish-on-different-OS deterministic.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Services.PackRelay;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Services.PackRelay
{
    public enum PublishPhase
    {
        Walking,
        Hashing,
        Uploading,
        Signing,
        Posting,
        Done,
    }

    /// <summary>
    /// One step in the publish flow. Reported via IProgress so the
    /// UI can render a live progress bar + status line without the
    /// orchestrator caring about presentation.
    /// </summary>
    public class PublishProgress
    {
        public PublishPhase Phase { get; set; }
        /// <summary>Files done in the current phase. 0 outside Hashing/Uploading.</summary>
        public int FilesDone { get; set; }
        /// <summary>Total files in the current phase. 0 outside Hashing/Uploading.</summary>
        public int FilesTotal { get; set; }
        /// <summary>Cumulative bytes processed in the current phase (uploaded or hashed).</summary>
        public long BytesDone { get; set; }
        /// <summary>Total bytes in the current phase.</summary>
        public long BytesTotal { get; set; }
        /// <summary>Most recently completed file's path, for the UI's status line.</summary>
        public string CurrentFile { get; set; }
    }

    /// <summary>
    /// What the orchestrator hands back on success. Mirrors what the
    /// UI's "published as X v1.2.3" success state needs to render.
    /// </summary>
    public class PublishResult
    {
        public string Slug { get; set; }
        public string Version { get; set; }
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
        /// <summary>True when the cloud returned 409 — the version was already on the catalog from a prior run. UI surfaces this as "already published" rather than a failure.</summary>
        public bool AlreadyPublished { get; set; }
    }

    /// <summary>
    /// Caller-supplied inputs. Separated into a POCO so the
    /// orchestrator stays decoupled from KitsuneCommand's Modpack
    /// entity (Stage 5's controller composes this from the modpack
    /// record + the PackRelay settings row).
    /// </summary>
    public class PublishRequest
    {
        /// <summary>Cloud-side slug — e.g. "kitsune-den". Lowercase a-z + digits + hyphens.</summary>
        public string Slug { get; set; }
        /// <summary>User-facing pack name — e.g. "KitsuneDen Curated Pack".</summary>
        public string DisplayName { get; set; }
        /// <summary>Semver: 1.2.3 or 1.2.3-beta. Must be new (the cloud rejects duplicates unless soft-success path triggers).</summary>
        public string Version { get; set; }
        /// <summary>Optional human-readable description; ≤20k chars.</summary>
        public string Description { get; set; }
        /// <summary>Optional tags. ≤20 entries, each ≤40 chars.</summary>
        public IReadOnlyList<string> Tags { get; set; }
        /// <summary>Folder names under ModsRoot — the modpack's bundled mods.</summary>
        public IReadOnlyList<string> ModFolderNames { get; set; }
        /// <summary>Absolute path to the 7DTD server's Mods/ directory.</summary>
        public string ModsRoot { get; set; }
        /// <summary>Publisher display name (manifest "publisher" field).</summary>
        public string Publisher { get; set; }
        /// <summary>Game-version tag — e.g. "V2.0". Free text, 1-40 chars.</summary>
        public string GameVersion { get; set; }
        /// <summary>publicKeyId from packrelay.cloud /account/keys, format <c>&lt;publisher&gt;/&lt;key-name&gt;</c>.</summary>
        public string PublicKeyId { get; set; }
        /// <summary>32-byte Ed25519 seed paired with the public key the cloud stores under <c>PublicKeyId</c>.</summary>
        public byte[] SigningKeySeed { get; set; }
    }

    public class PackRelayPublishService
    {
        private readonly IPackRelayClient _client;
        private readonly int _maxParallel;

        public PackRelayPublishService(IPackRelayClient client, int maxParallel = 8)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (maxParallel < 1) throw new ArgumentException("maxParallel must be >= 1", nameof(maxParallel));
            _maxParallel = maxParallel;
        }

        public async Task<PublishResult> PublishAsync(
            PublishRequest req,
            IProgress<PublishProgress> onProgress = null,
            CancellationToken ct = default)
        {
            ValidateRequest(req);

            // 1. Walking — enumerate every file under the bundled mods.
            onProgress?.Report(new PublishProgress { Phase = PublishPhase.Walking });
            var files = WalkBundle(req);

            // 2. Hashing — streaming SHA-256 every file. Sequential
            //    in v1: on typical NVMe + 5GB pack, hashing is CPU-
            //    bound at ~1 GB/s; parallelism here adds little vs
            //    code complexity. Re-evaluate if a profiler ever
            //    points at hashing.
            var totalBytes = files.Sum(f => f.Size);
            long hashBytesDone = 0;
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[i];
                f.Sha256 = Sha256Hasher.HashFile(f.AbsolutePath);
                hashBytesDone += f.Size;
                onProgress?.Report(new PublishProgress
                {
                    Phase = PublishPhase.Hashing,
                    FilesDone = i + 1,
                    FilesTotal = files.Count,
                    BytesDone = hashBytesDone,
                    BytesTotal = totalBytes,
                    CurrentFile = f.RelativePath,
                });
            }

            // 3. Uploading — content-addressed dedup. Two files with
            //    the same bytes upload once.
            var uniqueShas = files
                .GroupBy(f => f.Sha256)
                .Select(g => g.First())
                .ToList();
            await UploadMissingBlobsAsync(uniqueShas, totalBytes, onProgress, ct).ConfigureAwait(false);

            // 4. Signing — build manifest, canonicalize, sign.
            onProgress?.Report(new PublishProgress { Phase = PublishPhase.Signing });
            var signedManifest = BuildAndSignManifest(req, files);

            // 5. Posting.
            onProgress?.Report(new PublishProgress { Phase = PublishPhase.Posting });
            bool alreadyPublished = false;
            try
            {
                await _client.PublishVersionAsync(req.Slug, signedManifest.ToString(Formatting.None), ct).ConfigureAwait(false);
            }
            catch (PackRelayApiException ex) when (ex.ErrorCode == "duplicate_version" || ex.StatusCode == 409)
            {
                // Soft success — bytes are on the cloud, version is
                // in the catalog from a prior run. Don't fail the
                // user out; report it explicitly so the UI can show
                // "already published" rather than "succeeded".
                alreadyPublished = true;
            }

            // 6. Done.
            onProgress?.Report(new PublishProgress
            {
                Phase = PublishPhase.Done,
                FilesDone = files.Count,
                FilesTotal = files.Count,
                BytesDone = totalBytes,
                BytesTotal = totalBytes,
            });
            return new PublishResult
            {
                Slug = req.Slug,
                Version = req.Version,
                FileCount = files.Count,
                TotalSize = totalBytes,
                AlreadyPublished = alreadyPublished,
            };
        }

        // ---- Walking + file model ----

        /// <summary>One file in the bundle, mutated through the pipeline.</summary>
        private class FileWork
        {
            public string AbsolutePath;
            /// <summary>Forward-slash path relative to the pack root, e.g. <c>Mods/KitsuneCraft/ModInfo.xml</c>.</summary>
            public string RelativePath;
            public long Size;
            public string Sha256; // filled by Hashing phase
        }

        private static List<FileWork> WalkBundle(PublishRequest req)
        {
            var work = new List<FileWork>();
            foreach (var modName in req.ModFolderNames)
            {
                var modRoot = Path.Combine(req.ModsRoot, modName);
                if (!Directory.Exists(modRoot))
                {
                    // Hard error: caller (the panel) told us to bundle
                    // a mod that isn't on disk. Better to fail loudly
                    // before producing a wrong manifest than to ship
                    // a pack that's missing files.
                    throw new DirectoryNotFoundException(
                        "Modpack references mod folder '" + modName + "' which doesn't exist at " + modRoot);
                }
                foreach (var abs in Directory.EnumerateFiles(modRoot, "*", SearchOption.AllDirectories))
                {
                    // Skip the launcher's sidecar if a prior install
                    // dropped one here — it's local install metadata,
                    // not source content. Publishing it would create
                    // a chicken-and-egg manifest-of-manifest situation.
                    if (Path.GetFileName(abs).Equals("_packrelay-manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var rel = MakeRelativePath(req.ModsRoot, abs);
                    work.Add(new FileWork
                    {
                        AbsolutePath = abs,
                        RelativePath = "Mods/" + rel,
                        Size = new FileInfo(abs).Length,
                    });
                }
            }
            if (work.Count == 0)
            {
                throw new InvalidOperationException(
                    "Bundle is empty — no files found under any of the requested mod folders.");
            }
            return work;
        }

        private static string MakeRelativePath(string baseDir, string fullPath)
        {
            // Path.GetRelativePath is .NET Core+; on net48 we hand-strip.
            var baseDirNorm = baseDir.TrimEnd('\\', '/');
            if (!fullPath.StartsWith(baseDirNorm, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Path " + fullPath + " is not under " + baseDirNorm);
            }
            var rel = fullPath.Substring(baseDirNorm.Length).TrimStart('\\', '/');
            // Canonical-JSON output must be deterministic across
            // host OS; forward slashes are the spec.
            return rel.Replace('\\', '/');
        }

        // ---- Uploading ----

        private async Task UploadMissingBlobsAsync(
            List<FileWork> uniqueByHash,
            long totalBytes,
            IProgress<PublishProgress> onProgress,
            CancellationToken ct)
        {
            using (var sem = new SemaphoreSlim(_maxParallel))
            {
                long bytesDone = 0;
                int filesDone = 0;
                int filesTotal = uniqueByHash.Count;

                var tasks = uniqueByHash.Select(async f =>
                {
                    await sem.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        bool already = await _client.BlobExistsAsync(f.Sha256, ct).ConfigureAwait(false);
                        if (!already)
                        {
                            using (var stream = new FileStream(
                                f.AbsolutePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                64 * 1024,
                                FileOptions.SequentialScan))
                            {
                                await _client.UploadBlobAsync(f.Sha256, stream, f.Size, ct: ct).ConfigureAwait(false);
                            }
                        }
                        // Whether we uploaded or skipped, the file is
                        // counted as "done" for progress so the UI
                        // ticks toward 100% smoothly.
                        Interlocked.Add(ref bytesDone, f.Size);
                        var done = Interlocked.Increment(ref filesDone);
                        onProgress?.Report(new PublishProgress
                        {
                            Phase = PublishPhase.Uploading,
                            FilesDone = done,
                            FilesTotal = filesTotal,
                            BytesDone = Interlocked.Read(ref bytesDone),
                            BytesTotal = totalBytes,
                            CurrentFile = f.RelativePath,
                        });
                    }
                    finally
                    {
                        sem.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        // ---- Manifest build + sign ----

        private static JObject BuildAndSignManifest(PublishRequest req, List<FileWork> files)
        {
            // Build the unsigned manifest first. CanonicalJson sorts
            // keys at canonicalization time, so insertion order
            // doesn't affect the signed bytes — but I'll insert in
            // a stable order anyway to keep diffs of the raw JSON
            // (when debugging) readable.
            var filesArr = new JArray();
            foreach (var f in files)
            {
                filesArr.Add(new JObject
                {
                    ["path"] = f.RelativePath,
                    ["sha256"] = f.Sha256,
                    ["size"] = f.Size,
                });
            }

            var manifestUnsigned = new JObject
            {
                ["schemaVersion"] = 1,
                ["name"] = req.Slug,
                ["displayName"] = req.DisplayName,
                ["version"] = req.Version,
                ["game"] = "7d2d",
                ["gameVersion"] = req.GameVersion,
                ["publisher"] = req.Publisher,
                ["publishedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["files"] = filesArr,
            };
            if (!string.IsNullOrEmpty(req.Description))
            {
                manifestUnsigned["description"] = req.Description;
            }
            if (req.Tags != null && req.Tags.Count > 0)
            {
                var tags = new JArray();
                foreach (var t in req.Tags) tags.Add(t);
                manifestUnsigned["tags"] = tags;
            }

            // Sign the canonical bytes of the manifest-without-signature.
            // The cloud verifies by reconstructing the same canonical
            // bytes (strip signature, canonicalize, ed25519.verify).
            var canonical = CanonicalJson.Encode(manifestUnsigned);
            var sig = Ed25519Signer.Sign(canonical, req.SigningKeySeed);
            var sigHex = BytesToHex(sig);

            // Attach signature and return the signed manifest.
            // Cloud's signatureSchema requires exactly these three
            // fields under `signature`: algo=ed25519, publicKeyId,
            // value=hex.
            manifestUnsigned["signature"] = new JObject
            {
                ["algo"] = "ed25519",
                ["publicKeyId"] = req.PublicKeyId,
                ["value"] = sigHex,
            };
            return manifestUnsigned;
        }

        private static string BytesToHex(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                c[i * 2] = hex[bytes[i] >> 4];
                c[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(c);
        }

        // ---- Validation ----

        private static void ValidateRequest(PublishRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.Slug))
                throw new ArgumentException("Slug required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                throw new ArgumentException("DisplayName required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.Version))
                throw new ArgumentException("Version required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.Publisher))
                throw new ArgumentException("Publisher required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.GameVersion))
                throw new ArgumentException("GameVersion required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.PublicKeyId))
                throw new ArgumentException("PublicKeyId required", nameof(req));
            if (string.IsNullOrWhiteSpace(req.ModsRoot))
                throw new ArgumentException("ModsRoot required", nameof(req));
            if (req.ModFolderNames == null || req.ModFolderNames.Count == 0)
                throw new ArgumentException("ModFolderNames must contain at least one entry", nameof(req));
            if (req.SigningKeySeed == null || req.SigningKeySeed.Length != Ed25519Signer.PrivateKeySeedLength)
                throw new ArgumentException(
                    "SigningKeySeed must be " + Ed25519Signer.PrivateKeySeedLength + " bytes", nameof(req));
        }
    }
}
