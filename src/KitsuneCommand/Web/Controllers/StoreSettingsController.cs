using System.Web.Http;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;
using Newtonsoft.Json;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing Store feature settings.
    /// Admin-only access.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/settings/store")]
    public class StoreSettingsController : ApiController
    {
        private readonly ISettingsRepository _settingsRepo;
        private const string SettingsKey = StorePurchasePolicy.SettingsKey;

        public StoreSettingsController(ISettingsRepository settingsRepo)
        {
            _settingsRepo = settingsRepo;
        }

        /// <summary>
        /// Get current store settings.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(LoadSettings()));
        }

        /// <summary>
        /// Update store settings.
        /// </summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] StoreSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings body is required.");

            // Validate ranges
            if (settings.PurchaseCooldownSeconds < 0) settings.PurchaseCooldownSeconds = 0;
            if (settings.PurchaseCooldownSeconds > 86400) settings.PurchaseCooldownSeconds = 86400;
            if (settings.MaxDailyPurchases < 0) settings.MaxDailyPurchases = 0;
            if (settings.PriceMultiplier < 0.1) settings.PriceMultiplier = 0.1;
            if (settings.PriceMultiplier > 10.0) settings.PriceMultiplier = 10.0;

            try
            {
                var json = JsonConvert.SerializeObject(settings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out("[KitsuneCommand] Store settings updated and saved.");
                return Ok(ApiResponse.Ok("Store settings updated."));
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to save store settings: {ex.Message}");
                return InternalServerError();
            }
        }

        private StoreSettings LoadSettings() => StorePurchasePolicy.LoadSettings(_settingsRepo);
    }
}
