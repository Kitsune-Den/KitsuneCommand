using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Applies the Store settings to a purchase: the price after the multiplier, and whether
    /// the cooldown or the daily cap refuses it. Both purchase paths (in-game /buy and the
    /// panel's Buy for Player) check this before dropping anything, so a refusal costs nothing.
    /// </summary>
    public static class StorePurchasePolicy
    {
        /// <summary>Settings table key the Store settings are saved under.</summary>
        public const string SettingsKey = "Store";

        /// <summary>
        /// Reads the persisted StoreSettings. Falls back to defaults if not found or on error.
        /// </summary>
        public static StoreSettings LoadSettings(ISettingsRepository settingsRepo)
        {
            try
            {
                var json = settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<StoreSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { /* fall through to defaults */ }

            return new StoreSettings();
        }

        /// <summary>
        /// The price a player pays: <paramref name="basePrice"/> times the multiplier, rounded to
        /// the nearest point (halves round up). The multiplier is held to the 0.1–10 range the
        /// settings page allows, in case an older or hand-edited value is outside it.
        /// </summary>
        public static int EffectivePrice(int basePrice, StoreSettings settings)
        {
            var multiplier = settings?.PriceMultiplier ?? 1.0;
            if (double.IsNaN(multiplier)) multiplier = 1.0;
            multiplier = Math.Min(Math.Max(multiplier, 0.1), 10.0);

            var price = Math.Round(basePrice * multiplier, MidpointRounding.AwayFromZero);
            if (price <= 0) return 0;
            return price >= int.MaxValue ? int.MaxValue : (int)price;
        }

        /// <summary>
        /// Why the cooldown or daily cap refuses a purchase at <paramref name="nowUtc"/>, or null
        /// when it can go ahead. The daily cap counts purchases since 00:00 UTC, the same day
        /// boundary /signin uses.
        /// </summary>
        public static string LimitRefusal(StoreSettings settings, DateTime nowUtc, DateTime? lastPurchaseUtc, int purchasesToday)
        {
            if (settings == null) return null;

            if (settings.MaxDailyPurchases > 0 && purchasesToday >= settings.MaxDailyPurchases)
            {
                var resetsIn = nowUtc.Date.AddDays(1) - nowUtc;
                return $"Daily purchase limit reached ({settings.MaxDailyPurchases} per day). It resets in {FormatWait(resetsIn)}.";
            }

            if (settings.PurchaseCooldownSeconds > 0 && lastPurchaseUtc.HasValue)
            {
                var wait = lastPurchaseUtc.Value.AddSeconds(settings.PurchaseCooldownSeconds) - nowUtc;
                if (wait > TimeSpan.Zero)
                    return $"Purchase cooldown: the next purchase is allowed in {FormatWait(wait)}.";
            }

            return null;
        }

        /// <summary>
        /// <see cref="LimitRefusal(StoreSettings, DateTime, DateTime?, int)"/> for a player, reading
        /// their recent purchases from the history. Skips the queries for limits that are off.
        /// </summary>
        public static string LimitRefusal(StoreSettings settings, IPurchaseHistoryRepository purchases, string playerId, DateTime nowUtc)
        {
            if (settings == null) return null;

            var purchasesToday = settings.MaxDailyPurchases > 0 ? purchases.CountSince(playerId, nowUtc.Date) : 0;
            var lastPurchaseUtc = settings.PurchaseCooldownSeconds > 0 ? purchases.GetLastPurchaseUtc(playerId) : null;
            return LimitRefusal(settings, nowUtc, lastPurchaseUtc, purchasesToday);
        }

        /// <summary>A wait as "45s", "2m 5s" or "3h 20m", rounded up so it is never shown as 0.</summary>
        internal static string FormatWait(TimeSpan wait)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
            if (seconds < 60) return $"{seconds}s";
            if (seconds < 3600) return seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds / 60}m {seconds % 60}s";

            var minutes = (seconds + 59) / 60;
            return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
        }
    }
}
