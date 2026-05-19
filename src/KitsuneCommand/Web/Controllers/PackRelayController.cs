// REST surface for the PackRelay publish flow.
//
// Endpoints:
//   GET    /api/packrelay/settings           — redacted status
//   POST   /api/packrelay/settings           — save creds (any subset)
//   DELETE /api/packrelay/settings           — wipe creds + master
//   POST   /api/packrelay/publish/{modpackId} — kick off a publish; returns jobId
//   GET    /api/packrelay/jobs/{jobId}        — poll for status
//
// All endpoints require admin role (same gate as the Modpack admin
// API surface). The settings GET/POST/DELETE NEVER returns
// plaintext credentials over the wire — see PackRelaySettingsService
// for the redacted Status shape.
//
// Publish lifecycle:
//   1. POST publish/{modpackId} — controller composes a
//      PublishRequest from the modpack record + decrypted settings,
//      allocates a job via the tracker, kicks off PublishAsync on
//      a thread-pool task. Returns immediately with the jobId.
//   2. Frontend polls GET jobs/{jobId} every ~1s.
//   3. Worker task funnels PublishProgress events into the tracker.
//      Final state (Done | Error) freezes the snapshot.
//
// We deliberately do NOT support concurrent publishes — the tracker
// returns InvalidOperationException -> 409 Conflict if another
// publish is in flight. KC v1 is one publisher per panel, one
// publish at a time.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Services;
using KitsuneCommand.Services.PackRelay;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;
using Newtonsoft.Json;

namespace KitsuneCommand.Web.Controllers
{
    [Authorize]
    [RoutePrefix("api/packrelay")]
    public class PackRelayController : ApiController
    {
        // Cloud base URL. Fixed for v1; the launcher's tauri.conf.json
        // and the cloud's own /downloads page also hard-code this.
        // Future: settings field, multi-env support.
        private const string DefaultCloudUrl = "https://packrelay.cloud";

        // Game version stamped on every manifest. Matches the KC
        // README badge + the 7DTD V2.0 baseline this build is
        // pinned against. Move to settings when we need per-pack
        // overrides.
        private const string DefaultGameVersion = "V2.0";

        private readonly PackRelaySettingsService _settings;
        private readonly PackRelayPublishJobTracker _jobs;
        private readonly ModpackService _modpacks;
        private readonly ModManagerService _modManager;

        public PackRelayController(
            PackRelaySettingsService settings,
            PackRelayPublishJobTracker jobs,
            ModpackService modpacks,
            ModManagerService modManager)
        {
            _settings = settings;
            _jobs = jobs;
            _modpacks = modpacks;
            _modManager = modManager;
        }

        // ─── Settings ─────────────────────────────────────────────────────

        /// <summary>Redacted status of the publisher credentials.</summary>
        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_settings.GetStatus()));
        }

        /// <summary>
        /// Persist any subset of {apiToken, signingKey, publicKeyId,
        /// publisherSlug}. Each is optional; passing null/empty leaves
        /// the existing value alone. To wipe a single credential,
        /// DELETE the whole row + re-POST.
        /// </summary>
        public class SaveSettingsRequest
        {
            /// <summary>Plaintext API token from packrelay.cloud /account/tokens. Sent once over HTTPS; encrypted at rest.</summary>
            public string ApiToken { get; set; }
            /// <summary>Base64-encoded 32-byte Ed25519 private-key seed. Same format the cloud's /account/keys hands the publisher at generation time.</summary>
            public string SigningKeyBase64 { get; set; }
            /// <summary>The publicKeyId from the cloud, format <c>&lt;publisher&gt;/&lt;key-name&gt;</c>. Must be set together with the signing key.</summary>
            public string PublicKeyId { get; set; }
            /// <summary>Cloud-side pack slug for THIS panel's publish target. Required before the first publish; immutable after.</summary>
            public string PublisherSlug { get; set; }
        }

        [HttpPost]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SaveSettings([FromBody] SaveSettingsRequest body)
        {
            if (body == null) return Content(HttpStatusCode.BadRequest,
                ApiResponse.Error(400, "Request body required."));
            try
            {
                if (!string.IsNullOrWhiteSpace(body.ApiToken))
                {
                    _settings.SetApiToken(body.ApiToken.Trim());
                }
                if (!string.IsNullOrWhiteSpace(body.SigningKeyBase64))
                {
                    byte[] seed;
                    try
                    {
                        seed = Convert.FromBase64String(body.SigningKeyBase64.Trim());
                    }
                    catch (FormatException)
                    {
                        return Content(HttpStatusCode.BadRequest,
                            ApiResponse.Error(400, "signingKey must be valid base64."));
                    }
                    if (string.IsNullOrWhiteSpace(body.PublicKeyId))
                    {
                        return Content(HttpStatusCode.BadRequest,
                            ApiResponse.Error(400, "publicKeyId is required when setting the signing key."));
                    }
                    _settings.SetSigningKey(seed, body.PublicKeyId.Trim());
                }
                if (!string.IsNullOrWhiteSpace(body.PublisherSlug))
                {
                    _settings.SetPublisherSlug(body.PublisherSlug.Trim());
                }
                return Ok(ApiResponse.Ok(_settings.GetStatus()));
            }
            catch (ArgumentException ex)
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400, ex.Message));
            }
        }

        /// <summary>Wipe all PackRelay credentials + the encryption master key.</summary>
        [HttpDelete]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ResetSettings()
        {
            _settings.Reset();
            return Ok(ApiResponse.Ok("Settings cleared."));
        }

        // ─── Publish ──────────────────────────────────────────────────────

        /// <summary>
        /// Kick off a publish job for the given modpack. The modpack
        /// must already be saved as a draft or published; the
        /// publish flow walks the bundled mod folders verbatim.
        /// Returns 202 Accepted + the job id. Frontend polls /jobs/.
        /// </summary>
        [HttpPost]
        [Route("publish/{modpackId:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Publish(int modpackId)
        {
            var modpack = _modpacks.Get();
            if (modpack == null || modpack.Id != modpackId)
            {
                return Content(HttpStatusCode.NotFound,
                    ApiResponse.Error(404, "Modpack " + modpackId + " not found."));
            }

            List<string> modList;
            try
            {
                modList = string.IsNullOrEmpty(modpack.ModList)
                    ? new List<string>()
                    : JsonConvert.DeserializeObject<List<string>>(modpack.ModList);
            }
            catch
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400, "Modpack has a corrupt mod_list field."));
            }
            if (modList == null || modList.Count == 0)
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400, "Modpack has no bundled mods. Add at least one before publishing."));
            }

            PackRelayCredentials creds;
            try
            {
                creds = _settings.GetCredentials();
            }
            catch (InvalidOperationException ex)
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400, ex.Message));
            }
            if (string.IsNullOrWhiteSpace(creds.PublisherSlug))
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400,
                        "Set publisherSlug on the PackRelay settings tab before publishing."));
            }

            // Allocate the job before kicking off the worker so a
            // concurrent-publish race results in 409 instead of two
            // simultaneous uploads to the cloud.
            string jobId;
            try
            {
                jobId = _jobs.AllocateJob(modpackId);
            }
            catch (InvalidOperationException ex)
            {
                return Content(HttpStatusCode.Conflict,
                    ApiResponse.Error(409, ex.Message));
            }

            // Compose the orchestrator request. Publisher display name
            // is derived from the publicKeyId's leading segment —
            // matches what the cloud will index against.
            var publisher = ExtractPublisher(creds.PublicKeyId);
            var req = new PublishRequest
            {
                Slug = creds.PublisherSlug,
                DisplayName = modpack.Name,
                Version = modpack.Version,
                Description = modpack.Description,
                ModFolderNames = modList,
                ModsRoot = _modManager.GetModsPath(),
                Publisher = publisher,
                GameVersion = DefaultGameVersion,
                PublicKeyId = creds.PublicKeyId,
                SigningKeySeed = creds.SigningKeySeed,
            };

            // Fire-and-forget. Task exceptions get marshaled into the
            // tracker so the frontend's poll surfaces them; we don't
            // need to observe the Task ourselves.
            var capturedJobId = jobId;
            Task.Run(async () =>
            {
                try
                {
                    var progress = new Progress<PublishProgress>(p =>
                        _jobs.ReportProgress(capturedJobId, p));
                    using (var client = new PackRelayClient(DefaultCloudUrl, creds.ApiToken))
                    {
                        var orchestrator = new PackRelayPublishService(client);
                        var result = await orchestrator.PublishAsync(req, progress).ConfigureAwait(false);
                        _jobs.MarkDone(capturedJobId, result);
                    }
                }
                catch (PackRelayApiException ex)
                {
                    _jobs.MarkError(capturedJobId, ex.Message, ex.ErrorCode);
                }
                catch (Exception ex)
                {
                    _jobs.MarkError(capturedJobId, ex.Message, null);
                }
            });

            return Content(HttpStatusCode.Accepted, ApiResponse.Ok(new
            {
                jobId,
                modpackId,
            }));
        }

        // ─── Curator handoff (#152) ────────────────────────────────────────

        /// <summary>
        /// "Create new pack on packrelay.cloud" handoff (#152). Builds a
        /// snapshot of the user's installed mods + the current modpack
        /// hint, POSTs it anonymously to packrelay.cloud's draft-seed
        /// endpoint, and returns the one-shot claim URL the frontend
        /// opens in the user's default browser.
        ///
        /// Unlike the publish path, this endpoint does NOT need any
        /// PackRelay credentials configured on KC ~ the user signs in
        /// to packrelay.cloud in the browser after clicking the URL.
        /// The seed itself just carries (folderName, displayName,
        /// version) per mod; no sensitive material crosses the wire.
        ///
        /// We send the FULL installed-mods snapshot from disk (every
        /// folder under Mods/), not just the bundled-for-publish
        /// subset. Curators can trim in the cloud editor; the broader
        /// the suggestion list, the more useful the handoff.
        /// </summary>
        [HttpPost]
        [Route("draft-seed")]
        [RoleAuthorize("admin")]
        public async Task<IHttpActionResult> CreateDraftSeed()
        {
            var installedMods = _modManager.GetMods()
                .Select(m => new
                {
                    name = m.FolderName,
                    displayName = m.DisplayName,
                    version = m.Version ?? "",
                })
                .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (installedMods.Count == 0)
            {
                return Content(HttpStatusCode.BadRequest,
                    ApiResponse.Error(400,
                        "No mods installed under the Mods/ folder. Install at least one before sending a seed."));
            }

            // Optional pack-name + version hint from the saved modpack
            // record. The cloud's claim page uses this to pre-fill the
            // pack-creation form. Absent if the user hasn't saved a
            // draft modpack yet ~ they'll just type a name fresh.
            var modpack = _modpacks.Get();
            object packHint = null;
            if (modpack != null)
            {
                packHint = new
                {
                    name = modpack.Name,
                    version = modpack.Version,
                };
            }

            var payload = new
            {
                mods = installedMods,
                packHint,
            };

            string responseBody;
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KitsuneCommand-PackRelay/1.0");
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await http.PostAsync(DefaultCloudUrl + "/api/packs/draft-seed", content)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Content(HttpStatusCode.BadGateway,
                        ApiResponse.Error(502,
                            "Couldn't reach packrelay.cloud: " + ex.Message));
                }

                responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // Bubble up the cloud's error message (rate-limit,
                    // schema validation, etc.) so the user sees the
                    // real reason without having to crack open browser
                    // devtools.
                    return Content((HttpStatusCode)(int)response.StatusCode,
                        ApiResponse.Error((int)response.StatusCode,
                            "packrelay.cloud returned " + (int)response.StatusCode + ": " + responseBody));
                }
            }

            // Cloud's POST returns { ok, token, url, expiresAt }. We
            // pass url + expiresAt straight through to the frontend.
            // The frontend opens the URL and discards the token; we
            // don't need to track it here.
            CloudDraftSeedResponse parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<CloudDraftSeedResponse>(responseBody);
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.BadGateway,
                    ApiResponse.Error(502,
                        "Couldn't parse packrelay.cloud response: " + ex.Message));
            }

            return Ok(ApiResponse.Ok(new
            {
                url = parsed.Url,
                expiresAt = parsed.ExpiresAt,
                modCount = installedMods.Count,
            }));
        }

        private class CloudDraftSeedResponse
        {
            [JsonProperty("token")] public string Token { get; set; }
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("expiresAt")] public string ExpiresAt { get; set; }
        }

        /// <summary>Poll for job status. Frontend hits this every ~1s while the job's running.</summary>
        [HttpGet]
        [Route("jobs/{jobId}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Content(HttpStatusCode.BadRequest, ApiResponse.Error(400, "jobId required."));
            }
            var snap = _jobs.Get(jobId);
            if (snap == null)
            {
                return Content(HttpStatusCode.NotFound,
                    ApiResponse.Error(404, "No such PackRelay job (or it expired)."));
            }
            return Ok(ApiResponse.Ok(snap));
        }

        // ─── Internals ────────────────────────────────────────────────────

        /// <summary>
        /// Extract the publisher segment from a <c>&lt;publisher&gt;/&lt;key-name&gt;</c>
        /// publicKeyId. Matches the cloud's KEY_ID_RE pattern. Falls
        /// back to the full string when no slash is present — the
        /// cloud's manifest schema accepts any 1-80 char string for
        /// publisher, so a fallback can't reject a valid input.
        /// </summary>
        private static string ExtractPublisher(string publicKeyId)
        {
            if (string.IsNullOrEmpty(publicKeyId)) return "unknown";
            var idx = publicKeyId.IndexOf('/');
            return idx > 0 ? publicKeyId.Substring(0, idx) : publicKeyId;
        }
    }
}
