// HTTP transport for the publish flow. Pure plumbing — knows the
// cloud's auth conventions + endpoint shapes; doesn't know about
// canonical-JSON, signing, or file walking. The orchestrator
// (Stage 3) composes hashing + this client + the signer.
//
// Endpoint shapes (from PackRelayCloud src/app/api/v1/...):
//
//   GET  /api/v1/files/exists?hash=<sha256>
//        -> { exists: bool, size: number|null }
//        Cheap dedup probe. exists=true skips the upload.
//
//   PUT  /api/v1/files/put?pathname=packs/files/<sha256>
//        body: raw octet-stream
//        Single-shot upload. Cap is 4 MB (Vercel platform limit,
//        NOT the 16 MB I'd assumed). Files larger than that have
//        to go through multipart.
//
//   POST /api/v1/files/multipart/init
//        body: { pathname: "packs/files/<sha256>" }
//        -> { uploadId, key }
//
//   POST /api/v1/files/multipart/part?pathname&uploadId&partNumber
//        body: raw chunk bytes (default 16 MB; Pro cap is 100 MB)
//        Response header `etag` (also in body); we read body JSON
//        because the @vercel/blob server response format is stable
//        across SDK versions and ETag header presence isn't.
//
//   POST /api/v1/files/multipart/complete
//        body: { pathname, uploadId, parts: [{ etag, partNumber }] }
//        -> { ok, pathname, url, size }
//
//   POST /api/v1/packs/<slug>/versions
//        body: the SIGNED MANIFEST as JSON. signature lives inside
//        the manifest's `signature` field; cloud canonicalizes
//        (manifest minus signature) to verify.
//        -> 200 on success, 409 on duplicate version
//
// Auth: every endpoint accepts Bearer <api-token> OR session
// cookie. We always use Bearer because KC is publishing as the
// publisher's authenticated agent (the panel user). Token comes
// from the settings storage (Stage 4).
//
// Error model: every call surfaces the cloud's status code +
// `{ error: "..." }` body verbatim through PackRelayApiException.
// Retry policy is the orchestrator's job — the client never
// silently retries because hiding a 4xx (e.g. "version already
// exists") would mask important state from the caller.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KitsuneCommand.Services.PackRelay
{
    /// <summary>
    /// Thrown when the cloud returns a non-2xx response. Carries
    /// both the HTTP status code and the cloud's <c>{error, code?}</c>
    /// body so the orchestrator + UI layer can render an informative
    /// message instead of a generic "request failed".
    /// </summary>
    public class PackRelayApiException : Exception
    {
        public int StatusCode { get; }
        /// <summary>The cloud's machine-readable error code when it
        /// included one (e.g. "manifest_shape", "slug_mismatch",
        /// "duplicate_version"). Null when the response was missing or
        /// non-JSON.</summary>
        public string ErrorCode { get; }

        public PackRelayApiException(int statusCode, string message, string errorCode)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// The publish flow's view of the cloud. Extracted so the
    /// orchestrator (PackRelayPublishService) can be unit-tested
    /// against a Moq without standing up an HttpMessageHandler per
    /// test. PackRelayClient is the production implementation;
    /// other consumers (admin tooling, future automation) can mock
    /// freely.
    /// </summary>
    public interface IPackRelayClient
    {
        Task<bool> BlobExistsAsync(string sha256, CancellationToken ct = default);
        Task PutBlobAsync(string sha256, Stream content, long size, CancellationToken ct = default);
        Task UploadBlobAsync(string sha256, Stream content, long size, int chunkBytes = PackRelayClient.DefaultMultipartChunkBytes, CancellationToken ct = default);
        Task<JObject> PublishVersionAsync(string slug, string signedManifestJson, CancellationToken ct = default);
    }

    public class PackRelayClient : IPackRelayClient, IDisposable
    {
        /// <summary>
        /// Single-shot upload cap. Vercel platform limit (NOT a
        /// PackRelay choice). Files larger than this MUST go
        /// through the multipart endpoints.
        /// </summary>
        public const long SingleShotMaxBytes = 4L * 1024 * 1024;

        /// <summary>
        /// Default multipart chunk size. 16 MB matches the launcher's
        /// download chunking + sits well under Vercel Pro's 100 MB
        /// streaming cap.
        /// </summary>
        public const int DefaultMultipartChunkBytes = 16 * 1024 * 1024;

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        /// <summary>
        /// Construct the client with an explicit API base URL and
        /// bearer token. KC's settings service (Stage 4) supplies
        /// both at publish-kickoff time.
        /// </summary>
        /// <param name="apiBaseUrl">e.g. <c>https://packrelay.cloud</c> — trailing slash optional.</param>
        /// <param name="apiToken">Personal API token minted at packrelay.cloud /account/tokens.</param>
        public PackRelayClient(string apiBaseUrl, string apiToken)
            : this(apiBaseUrl, apiToken, null) { }

        /// <summary>
        /// Test-friendly constructor: caller supplies the HttpClient
        /// (with a mocked HttpMessageHandler for unit tests). When
        /// <paramref name="injectedClient"/> is null we own + dispose
        /// a default HttpClient; when it's supplied we don't.
        /// </summary>
        public PackRelayClient(string apiBaseUrl, string apiToken, HttpClient injectedClient)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                throw new ArgumentException("apiBaseUrl required", nameof(apiBaseUrl));
            if (string.IsNullOrWhiteSpace(apiToken))
                throw new ArgumentException("apiToken required", nameof(apiToken));

            if (injectedClient != null)
            {
                _http = injectedClient;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient
                {
                    // No global timeout — multipart uploads of large
                    // files legitimately take >100s. Per-request
                    // cancellation tokens are the right knob.
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                _ownsHttp = true;
            }

            _http.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiToken);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "KitsuneCommand-PackRelay-Publisher/1.0");
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ---- Existence probe ----

        /// <summary>
        /// Returns true when the cloud already has a blob at
        /// <c>packs/files/&lt;sha256&gt;</c>. Used by the orchestrator
        /// to skip uploads for content-addressed dedup; idempotent
        /// re-runs of the same publish only pay the manifest POST.
        /// </summary>
        public async Task<bool> BlobExistsAsync(string sha256, CancellationToken ct = default)
        {
            ValidateSha(sha256);
            var url = "api/v1/files/exists?hash=" + Uri.EscapeDataString(sha256);
            using (var res = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                await ThrowIfErrorAsync(res).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);
                return json.Value<bool>("exists");
            }
        }

        // ---- Single-shot upload ----

        /// <summary>
        /// Upload <paramref name="content"/> to <c>packs/files/&lt;sha&gt;</c>
        /// via the single-shot endpoint. The stream is read once,
        /// from current position to EOF; caller manages lifetime
        /// and rewind. Bytes must already hash to <paramref name="sha256"/>;
        /// the cloud trusts the addressed pathname and the launcher
        /// hash-verifies at install time.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// When <paramref name="size"/> exceeds <see cref="SingleShotMaxBytes"/>.
        /// Use <see cref="UploadBlobAsync"/> for size-agnostic uploads.
        /// </exception>
        public async Task PutBlobAsync(
            string sha256,
            Stream content,
            long size,
            CancellationToken ct = default)
        {
            ValidateSha(sha256);
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (size < 0) throw new ArgumentException("size must be non-negative", nameof(size));
            if (size > SingleShotMaxBytes)
            {
                throw new ArgumentException(
                    "Single-shot endpoint caps at " + SingleShotMaxBytes +
                    " bytes; use UploadBlobAsync for larger files.",
                    nameof(size));
            }

            var url = "api/v1/files/put?pathname=" +
                Uri.EscapeDataString("packs/files/" + sha256);
            using (var streamContent = new StreamContent(content))
            {
                streamContent.Headers.ContentType =
                    new MediaTypeHeaderValue("application/octet-stream");
                streamContent.Headers.ContentLength = size;
                using (var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = streamContent })
                using (var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    await ThrowIfErrorAsync(res).ConfigureAwait(false);
                }
            }
        }

        // ---- Multipart upload ----

        /// <summary>
        /// Initialize a multipart upload. Returns the <c>uploadId</c>
        /// the subsequent PartAsync + CompleteAsync calls need.
        /// </summary>
        public async Task<string> MultipartInitAsync(string sha256, CancellationToken ct = default)
        {
            ValidateSha(sha256);
            var body = new JObject { ["pathname"] = "packs/files/" + sha256 };
            using (var content = JsonContent(body))
            using (var res = await _http.PostAsync("api/v1/files/multipart/init", content, ct).ConfigureAwait(false))
            {
                await ThrowIfErrorAsync(res).ConfigureAwait(false);
                var json = JObject.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                var uploadId = json.Value<string>("uploadId");
                if (string.IsNullOrEmpty(uploadId))
                {
                    throw new PackRelayApiException(
                        (int)res.StatusCode,
                        "Cloud returned 200 from /multipart/init with no uploadId.",
                        null);
                }
                return uploadId;
            }
        }

        /// <summary>
        /// Upload one part of a multipart upload. Returns the ETag
        /// the cloud assigned to this part; caller collects ETags
        /// and passes them to <see cref="MultipartCompleteAsync"/>.
        /// Parts are 1-indexed per S3 multipart spec.
        /// </summary>
        public async Task<string> MultipartPartAsync(
            string sha256,
            string uploadId,
            int partNumber,
            Stream chunk,
            long chunkBytes,
            CancellationToken ct = default)
        {
            ValidateSha(sha256);
            if (string.IsNullOrEmpty(uploadId))
                throw new ArgumentException("uploadId required", nameof(uploadId));
            if (partNumber < 1)
                throw new ArgumentException("partNumber is 1-indexed", nameof(partNumber));

            var url = "api/v1/files/multipart/part?pathname=" +
                Uri.EscapeDataString("packs/files/" + sha256) +
                "&uploadId=" + Uri.EscapeDataString(uploadId) +
                "&partNumber=" + partNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using (var streamContent = new StreamContent(chunk))
            {
                streamContent.Headers.ContentType =
                    new MediaTypeHeaderValue("application/octet-stream");
                streamContent.Headers.ContentLength = chunkBytes;
                using (var res = await _http.PostAsync(url, streamContent, ct).ConfigureAwait(false))
                {
                    await ThrowIfErrorAsync(res).ConfigureAwait(false);
                    var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(body);
                    // The @vercel/blob server response uses `etag`
                    // lowercase. Don't fall back to the HTTP ETag
                    // header — that's @vercel/blob's underlying S3
                    // ETag which may differ from what the complete
                    // endpoint expects.
                    var etag = json.Value<string>("etag");
                    if (string.IsNullOrEmpty(etag))
                    {
                        throw new PackRelayApiException(
                            (int)res.StatusCode,
                            "Cloud returned 200 from /multipart/part with no etag.",
                            null);
                    }
                    return etag;
                }
            }
        }

        /// <summary>
        /// Finalize a multipart upload. <paramref name="parts"/> must
        /// be in part-number order; pass the ETag strings returned
        /// by each <see cref="MultipartPartAsync"/> call. The cloud
        /// assembles the chunks into the blob keyed at
        /// <c>packs/files/&lt;sha&gt;</c>.
        /// </summary>
        public async Task MultipartCompleteAsync(
            string sha256,
            string uploadId,
            MultipartPart[] parts,
            CancellationToken ct = default)
        {
            ValidateSha(sha256);
            if (string.IsNullOrEmpty(uploadId))
                throw new ArgumentException("uploadId required", nameof(uploadId));
            if (parts == null || parts.Length == 0)
                throw new ArgumentException("at least one part required", nameof(parts));

            var partsArr = new JArray();
            foreach (var p in parts)
            {
                partsArr.Add(new JObject
                {
                    ["etag"] = p.ETag,
                    ["partNumber"] = p.PartNumber,
                });
            }
            var body = new JObject
            {
                ["pathname"] = "packs/files/" + sha256,
                ["uploadId"] = uploadId,
                ["parts"] = partsArr,
            };
            using (var content = JsonContent(body))
            using (var res = await _http.PostAsync("api/v1/files/multipart/complete", content, ct).ConfigureAwait(false))
            {
                await ThrowIfErrorAsync(res).ConfigureAwait(false);
            }
        }

        // ---- Smart upload (orchestrator-facing entry point) ----

        /// <summary>
        /// Size-agnostic blob upload. Routes to <see cref="PutBlobAsync"/>
        /// when content fits the single-shot 4 MB cap; falls through
        /// to multipart-with-chunks for everything bigger. The
        /// orchestrator's hot path uses this.
        ///
        /// Caller still must supply <paramref name="size"/> (we won't
        /// seek the stream to measure) so partitioning is deterministic
        /// before the first byte goes over the wire.
        /// </summary>
        public async Task UploadBlobAsync(
            string sha256,
            Stream content,
            long size,
            int chunkBytes = DefaultMultipartChunkBytes,
            CancellationToken ct = default)
        {
            if (size <= SingleShotMaxBytes)
            {
                await PutBlobAsync(sha256, content, size, ct).ConfigureAwait(false);
                return;
            }
            if (chunkBytes < 1024 * 1024)
            {
                // Sanity: chunks smaller than 1 MB blow our part
                // count into the thousands for typical GB-scale mods.
                // S3 multipart caps at 10000 parts per upload.
                throw new ArgumentException(
                    "chunkBytes must be >= 1 MB to keep part count manageable.",
                    nameof(chunkBytes));
            }

            var uploadId = await MultipartInitAsync(sha256, ct).ConfigureAwait(false);
            var parts = new System.Collections.Generic.List<MultipartPart>();
            var buffer = new byte[chunkBytes];
            long remaining = size;
            int partNumber = 1;
            while (remaining > 0)
            {
                int targetThisPart = (int)Math.Min(remaining, chunkBytes);
                // Read may return less than requested even on a
                // perfectly cooperative stream; loop until we've
                // filled targetThisPart.
                int filled = 0;
                while (filled < targetThisPart)
                {
                    int n = await content.ReadAsync(buffer, filled, targetThisPart - filled, ct).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw new InvalidOperationException(
                            "Stream ended " + (remaining - filled) +
                            " bytes short of the declared size " + size + ".");
                    }
                    filled += n;
                }
                using (var ms = new MemoryStream(buffer, 0, filled, writable: false))
                {
                    var etag = await MultipartPartAsync(sha256, uploadId, partNumber, ms, filled, ct)
                        .ConfigureAwait(false);
                    parts.Add(new MultipartPart(etag, partNumber));
                }
                remaining -= filled;
                partNumber++;
            }
            await MultipartCompleteAsync(sha256, uploadId, parts.ToArray(), ct).ConfigureAwait(false);
        }

        // ---- Publish version ----

        /// <summary>
        /// POST the signed manifest to <c>/api/v1/packs/&lt;slug&gt;/versions</c>.
        /// The signature must already be embedded in the manifest's
        /// <c>signature</c> field — the cloud canonicalizes (manifest
        /// minus signature) to verify. 200/201 is success; any other
        /// status throws <see cref="PackRelayApiException"/>.
        /// </summary>
        public async Task<JObject> PublishVersionAsync(
            string slug,
            string signedManifestJson,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(slug)) throw new ArgumentException("slug required", nameof(slug));
            if (string.IsNullOrEmpty(signedManifestJson))
                throw new ArgumentException("signedManifestJson required", nameof(signedManifestJson));

            var url = "api/v1/packs/" + Uri.EscapeDataString(slug) + "/versions";
            using (var content = new StringContent(signedManifestJson, Encoding.UTF8, "application/json"))
            using (var res = await _http.PostAsync(url, content, ct).ConfigureAwait(false))
            {
                await ThrowIfErrorAsync(res).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return string.IsNullOrEmpty(body) ? new JObject() : JObject.Parse(body);
            }
        }

        // ---- Internals ----

        private static HttpContent JsonContent(JToken body)
        {
            return new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        private static async Task ThrowIfErrorAsync(HttpResponseMessage res)
        {
            if (res.IsSuccessStatusCode) return;
            string body = "";
            try
            {
                body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                // Body read failed; status is still the primary signal.
            }
            string message = null;
            string code = null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var json = JObject.Parse(body);
                    message = json.Value<string>("error");
                    code = json.Value<string>("code");
                }
                catch
                {
                    // Non-JSON response (HTML error page, plain text).
                    // Fall through to the raw body as message.
                }
            }
            if (string.IsNullOrEmpty(message))
            {
                message = string.IsNullOrEmpty(body) ? res.ReasonPhrase : body;
            }
            throw new PackRelayApiException((int)res.StatusCode, message ?? "request failed", code);
        }

        private static void ValidateSha(string sha256)
        {
            if (string.IsNullOrEmpty(sha256))
                throw new ArgumentException("sha256 required", nameof(sha256));
            if (sha256.Length != 64)
                throw new ArgumentException("sha256 must be 64 chars; got " + sha256.Length, nameof(sha256));
            for (int i = 0; i < sha256.Length; i++)
            {
                char c = sha256[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) throw new ArgumentException(
                    "sha256 must be lowercase hex; bad char at index " + i, nameof(sha256));
            }
        }
    }

    /// <summary>
    /// One part of a multipart upload. ETag is the opaque string the
    /// cloud handed back from <c>/files/multipart/part</c>;
    /// PartNumber is 1-indexed.
    /// </summary>
    public struct MultipartPart
    {
        public readonly string ETag;
        public readonly int PartNumber;
        public MultipartPart(string etag, int partNumber)
        {
            ETag = etag;
            PartNumber = partNumber;
        }
    }
}
