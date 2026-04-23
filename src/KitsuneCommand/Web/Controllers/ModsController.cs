using System;
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

        public ModsController(ModManagerService modService)
        {
            _modService = modService;
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
        /// </summary>
        [HttpPost]
        [Route("upload")]
        [RoleAuthorize("admin")]
        public async Task<IHttpActionResult> UploadMod()
        {
            try
            {
                if (!Request.Content.IsMimeMultipartContent())
                    return BadRequest("Multipart content required.");

                var provider = new MultipartMemoryStreamProvider();
                await Request.Content.ReadAsMultipartAsync(provider);

                foreach (var content in provider.Contents)
                {
                    var fileName = content.Headers.ContentDisposition?.FileName?.Trim('"');
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Only ZIP files are accepted.");

                    using (var stream = await content.ReadAsStreamAsync())
                    {
                        var result = _modService.UploadMod(stream, fileName);
                        return Ok(ApiResponse.Ok(result));
                    }
                }

                return BadRequest("No file found in upload.");
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to upload mod: {ex.Message}"));
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
