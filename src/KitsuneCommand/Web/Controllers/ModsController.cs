using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing server mods.
    /// Supports listing, uploading (ZIP), deleting, and enabling/disabling mods.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/mods")]
    public class ModsController : ApiController
    {
        private readonly ModManagerService _modService;
        private readonly ModUpdateService _updateService;

        public ModsController(ModManagerService modService, ModUpdateService updateService)
        {
            _modService = modService;
            _updateService = updateService;
        }

        /// <summary>
        /// Cross-reference every installed mod against Nexus by exact name match
        /// and surface available updates. Read-only — no downloads, no in-place
        /// replacements. The frontend shows a badge + Nexus link per row; admin
        /// re-downloads + uploads through the existing flow if they want to update.
        /// </summary>
        [HttpPost]
        [Route("check-updates")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CheckForUpdates()
        {
            try
            {
                var results = _updateService.CheckAll();
                return Ok(ApiResponse.Ok(results));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to check for updates: {ex.Message}"));
            }
        }

        /// <summary>
        /// List all installed mods with metadata.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetMods()
        {
            try
            {
                var mods = _modService.GetMods();
                return Ok(ApiResponse.Ok(mods));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to list mods: {ex.Message}"));
            }
        }

        /// <summary>
        /// Upload a mod as a ZIP file. Returns a ModUploadResult describing every
        /// mod installed from the zip (supports mod packs) plus any warnings
        /// surfaced during extraction (replaced-existing, skipped-protected,
        /// no-ModInfo fallback, etc.).
        ///
        /// Uses <see cref="MultipartFileStreamProvider"/> so the upload streams to
        /// disk in constant memory — large modpacks (500MB+) no longer risk
        /// OOM-killing the host process.
        /// </summary>
        [HttpPost]
        [Route("upload")]
        [RoleAuthorize("admin")]
        public async Task<IHttpActionResult> UploadMod()
        {
            if (!Request.Content.IsMimeMultipartContent())
                return BadRequest("Multipart content required.");

            var tempDir = Path.Combine(Path.GetTempPath(), "kc-mod-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var provider = new MultipartFileStreamProvider(tempDir);
                await Request.Content.ReadAsMultipartAsync(provider);

                foreach (var file in provider.FileData)
                {
                    var origFileName = file.Headers.ContentDisposition?.FileName?.Trim('"');
                    if (string.IsNullOrEmpty(origFileName))
                        continue;

                    if (!origFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Only ZIP files are accepted.");

                    using (var stream = File.OpenRead(file.LocalFileName))
                    {
                        var result = _modService.UploadMod(stream, origFileName);
                        return Ok(ApiResponse.Ok(result));
                    }
                }

                return BadRequest("No file found in upload.");
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to upload mod: {ex.Message}"));
            }
            finally
            {
                // Best-effort cleanup of the streamed temp file(s). The mod service
                // has already copied the zip contents into the Mods/ directory by
                // this point, so the temp file is no longer needed either way.
                try { Directory.Delete(tempDir, true); }
                catch { /* swallowed — temp cleanup must never break the response */ }
            }
        }

        /// <summary>
        /// Delete a mod by folder name.
        /// </summary>
        [HttpDelete]
        [Route("{modName}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteMod(string modName)
        {
            try
            {
                _modService.DeleteMod(modName);
                return Ok(ApiResponse.Ok($"Mod '{modName}' deleted. Server restart required."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponse.Error(403, ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Ok(ApiResponse.Error(404, ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to delete mod: {ex.Message}"));
            }
        }

        /// <summary>
        /// Toggle a mod enabled/disabled.
        /// </summary>
        [HttpPost]
        [Route("{modName}/toggle")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ToggleMod(string modName)
        {
            try
            {
                _modService.ToggleMod(modName);
                return Ok(ApiResponse.Ok($"Mod '{modName}' toggled. Server restart required."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponse.Error(403, ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Ok(ApiResponse.Error(404, ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to toggle mod: {ex.Message}"));
            }
        }
    }
}
