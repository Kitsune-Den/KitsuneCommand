using System;
using System.Collections.Generic;
using System.Web.Http;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for reading and writing the serverconfig.xml file.
    /// Provides both structured property access and raw XML editing.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/config")]
    public class ConfigController : ApiController
    {
        private readonly ServerConfigService _configService;

        public ConfigController(ServerConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Get current config properties, field definitions, and config file path.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetConfig()
        {
            try
            {
                var properties = _configService.ReadConfig();
                var groups = _configService.GetFieldDefinitions();
                var configPath = _configService.GetConfigPath();

                return Ok(ApiResponse.Ok(new
                {
                    properties,
                    groups,
                    configPath,
                    is30 = _configService.GameSupportsSandboxCode(),
                    needsMigration = _configService.NeedsMigrationTo30()
                }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to read config: {ex.Message}"));
            }
        }

        /// <summary>
        /// Get the raw XML content of the config file.
        /// </summary>
        [HttpGet]
        [Route("raw")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetRawXml()
        {
            try
            {
                var xml = _configService.ReadRawXml();
                return Ok(ApiResponse.Ok(new { xml }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to read config: {ex.Message}"));
            }
        }

        /// <summary>
        /// Update config properties. Creates a backup before writing.
        /// </summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SaveConfig([FromBody] Dictionary<string, string> properties)
        {
            if (properties == null || properties.Count == 0)
                return BadRequest("Properties are required.");

            try
            {
                _configService.SaveConfig(properties);
                return Ok(ApiResponse.Ok("Config saved. A backup was created. Changes require a server restart to take effect."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to save config: {ex.Message}"));
            }
        }

        /// <summary>
        /// Save raw XML content to the config file. Creates a backup before writing.
        /// </summary>
        [HttpPut]
        [Route("raw")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SaveRawXml([FromBody] RawXmlRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Xml))
                return BadRequest("XML content is required.");

            try
            {
                _configService.SaveRawXml(request.Xml);
                return Ok(ApiResponse.Ok("Config saved from raw XML. A backup was created. Changes require a server restart."));
            }
            catch (System.Xml.XmlException ex)
            {
                return Ok(ApiResponse.Error(400, $"Invalid XML: {ex.Message}"));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to save config: {ex.Message}"));
            }
        }

        /// <summary>
        /// List available world names.
        /// </summary>
        [HttpGet]
        [Route("worlds")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetWorlds()
        {
            try
            {
                var worlds = _configService.GetAvailableWorlds();
                return Ok(ApiResponse.Ok(worlds));
            }
            catch
            {
                return Ok(ApiResponse.Ok(new List<string> { "Navezgane" }));
            }
        }

        /// <summary>
        /// Migrate serverconfig.xml to the 7D2D 3.0 layout: comment out the sandbox-governed
        /// properties (3.0 reads those from SandboxCode) and ensure a SandboxCode property
        /// exists. Refuses on a non-3.0 server. Backs up before writing; idempotent.
        /// </summary>
        [HttpPost]
        [Route("migrate-3.0")]
        [RoleAuthorize("admin")]
        public IHttpActionResult MigrateTo30()
        {
            if (!_configService.GameSupportsSandboxCode())
                return Ok(ApiResponse.Error(400, "This server isn't running 7D2D 3.0 — nothing to migrate (the 3.0 Sandbox system isn't present)."));

            try
            {
                var result = _configService.MigrateConfigTo30();
                var message = result.Changed
                    ? $"Migrated to 3.0: commented out {result.Neutralized.Count} sandbox-governed setting(s)"
                      + (result.AddedSandboxCode ? " and added SandboxCode" : "")
                      + ". A backup was saved. Paste your Sandbox code, then restart to apply."
                    : "Already on the 3.0 layout — nothing to change.";
                return Ok(ApiResponse.Ok(new
                {
                    result.Changed,
                    result.AddedSandboxCode,
                    result.Neutralized,
                    result.BackupPath,
                    message
                }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Migration failed: {ex.Message}"));
            }
        }
    }

    public class RawXmlRequest
    {
        public string Xml { get; set; }
    }
}
