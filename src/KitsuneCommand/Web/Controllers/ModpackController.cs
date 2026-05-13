using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;
using Newtonsoft.Json;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// REST surface for the player-facing modpack.
    ///
    /// Admin endpoints (require admin role): browse + edit + publish workflow.
    /// Two endpoints are intentionally <c>[AllowAnonymous]</c> — those are
    /// what the login page (no-auth) calls to surface the download CTA.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/modpack")]
    public class ModpackController : ApiController
    {
        private readonly ModpackService _service;
        private readonly ModManagerService _modManager;

        public ModpackController(ModpackService service, ModManagerService modManager)
        {
            _service = service;
            _modManager = modManager;
        }

        // ─── Admin: state + edit ──────────────────────────────────────────

        /// <summary>
        /// Full state for the admin UI: the current modpack record (if any)
        /// plus the list of installed mods (the picker's source data).
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Get()
        {
            var record = _service.Get();
            var installedMods = _modManager.GetMods()
                .Select(m => new { name = m.FolderName, displayName = m.DisplayName, version = m.Version })
                .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Parse modList JSON eagerly so the frontend doesn't need to do it.
            List<string> modList = null;
            if (record != null && !string.IsNullOrEmpty(record.ModList))
            {
                try { modList = JsonConvert.DeserializeObject<List<string>>(record.ModList); }
                catch { modList = new List<string>(); }
            }

            return Ok(ApiResponse.Ok(new
            {
                modpack = record,
                modList = modList ?? new List<string>(),
                installedMods,
            }));
        }

        public class SaveDraftRequest
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public List<string> ModList { get; set; }
            public string Description { get; set; }
        }

        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Save([FromBody] SaveDraftRequest body)
        {
            if (body == null) return BadRequest("Request body is required.");
            try
            {
                var record = _service.SaveDraft(body.Name, body.Version, body.ModList, body.Description);
                return Ok(ApiResponse.Ok(record));
            }
            catch (ArgumentException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (InvalidOperationException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to save modpack: {ex.Message}")); }
        }

        [HttpPost]
        [Route("build")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Build()
        {
            try
            {
                var record = _service.BuildZip();
                return Ok(ApiResponse.Ok(record));
            }
            catch (InvalidOperationException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to build modpack: {ex.Message}")); }
        }

        [HttpPost]
        [Route("publish")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Publish()
        {
            try
            {
                var record = _service.Publish();
                return Ok(ApiResponse.Ok(record));
            }
            catch (InvalidOperationException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to publish modpack: {ex.Message}")); }
        }

        [HttpPost]
        [Route("archive")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Archive()
        {
            try
            {
                var record = _service.Archive();
                return Ok(ApiResponse.Ok(record));
            }
            catch (InvalidOperationException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to archive modpack: {ex.Message}")); }
        }

        [HttpPost]
        [Route("unarchive")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Unarchive()
        {
            try
            {
                var record = _service.Unarchive();
                return Ok(ApiResponse.Ok(record));
            }
            catch (InvalidOperationException ex) { return Ok(ApiResponse.Error(400, ex.Message)); }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to unarchive modpack: {ex.Message}")); }
        }

        [HttpDelete]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Delete()
        {
            try
            {
                _service.Delete();
                return Ok(ApiResponse.Ok("Modpack deleted."));
            }
            catch (Exception ex) { return Ok(ApiResponse.Error(500, $"Failed to delete modpack: {ex.Message}")); }
        }

        // ─── Public: anonymous metadata + download ────────────────────────

        /// <summary>
        /// Metadata for the published modpack, or a 404-shaped envelope if
        /// none is published. Login page calls this to render the CTA.
        /// No auth — anyone hitting the panel sees it.
        /// </summary>
        [HttpGet]
        [Route("public")]
        [AllowAnonymous]
        public IHttpActionResult GetPublic()
        {
            var record = _service.Get();
            if (record == null || record.Status != "published")
            {
                return Ok(ApiResponse.Error(404, "No modpack is currently published."));
            }
            return Ok(ApiResponse.Ok(new
            {
                name = record.Name,
                version = record.Version,
                sizeBytes = record.SizeBytes,
                modCount = record.ModCount,
                description = record.Description,
            }));
        }

        /// <summary>
        /// Streams the published modpack zip. Increments the download counter
        /// before serving so a successful HTTP 200 implies the counter moved.
        /// No auth — anyone with the link gets the file.
        /// </summary>
        [HttpGet]
        [Route("public/download")]
        [AllowAnonymous]
        public HttpResponseMessage Download()
        {
            var (record, stream) = _service.OpenPublishedForDownload();
            if (record == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("No modpack is currently published.")
                };
            }
            if (stream == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("Modpack metadata exists but the file is missing on disk. Rebuild from the admin panel.")
                };
            }

            _service.IncrementDownloadCount(record.Id);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = record.Filename };
            response.Content.Headers.ContentLength = stream.Length;
            return response;
        }
    }
}
