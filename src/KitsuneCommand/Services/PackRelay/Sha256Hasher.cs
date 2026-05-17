// Streaming SHA-256. System.Security.Cryptography.SHA256 + a 64KB
// buffer so a 4GB Unity asset bundle doesn't OOM the panel.
//
// Output convention: 64-char lowercase hex string. Matches:
//   - sha256sum CLI output (modulo the trailing filename)
//   - Node's crypto.createHash('sha256').update(...).digest('hex')
//   - .NET's BitConverter.ToString(...).Replace("-","").ToLowerInvariant()
// All three need to round-trip identically because the cloud's
// /api/v1/files/exists check is keyed by this exact string.
//
// We hash files (the dominant case) AND bytes (the manifest signing
// hot path uses the canonical bytes directly). Both methods route
// through SHA256.Create() because TransformFinalBlock + TransformBlock
// is the right API for streaming on net48 — IncrementalHash exists in
// .NET Standard 2.0+ but adds nothing over the older pattern here.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KitsuneCommand.Services.PackRelay
{
    public static class Sha256Hasher
    {
        // 64 KB. Big enough that the syscall overhead per chunk is
        // negligible vs the hash compute, small enough that we never
        // hold more than that resident even when hashing huge files.
        // Same chunk size pack-cli + the launcher's install loop use.
        private const int ReadBufferBytes = 64 * 1024;

        /// <summary>
        /// Hash a file by streaming. Returns the 64-char lowercase hex
        /// digest matching <c>sha256sum &lt;path&gt;</c>.
        /// </summary>
        public static string HashFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ReadBufferBytes,
                FileOptions.SequentialScan))
            {
                return HashStream(fs);
            }
        }

        /// <summary>
        /// Hash an arbitrary readable stream (file, memory, network).
        /// Reads to EOF; the caller is responsible for stream lifetime
        /// and for rewinding if they need to re-read after.
        /// </summary>
        public static string HashStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var hasher = SHA256.Create())
            {
                var buffer = new byte[ReadBufferBytes];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.TransformBlock(buffer, 0, read, null, 0);
                }
                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHexLower(hasher.Hash);
            }
        }

        /// <summary>
        /// Hash a single byte array. Convenience for short inputs
        /// (manifest canonical bytes, API tokens, etc.) where the
        /// streaming machinery is unnecessary.
        /// </summary>
        public static string HashBytes(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            using (var hasher = SHA256.Create())
            {
                return ToHexLower(hasher.ComputeHash(data));
            }
        }

        private static string ToHexLower(byte[] hash)
        {
            // BitConverter would work but it inserts dashes and uses
            // uppercase; the manual loop is a one-time write that
            // avoids two allocations + a replace + a ToLower pass.
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
