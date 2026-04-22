using System;
using HarmonyLib;

namespace KitsuneTraderUnlock
{
    public class ModEntry : IModApi
    {
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            Log.Out("[KitsuneTraderUnlock] Initializing...");

            try
            {
                _harmony = new Harmony("com.kitsunetraderunlock");
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                Log.Out("[KitsuneTraderUnlock] Harmony patches applied. Client-side trader area checks will return false.");
                Log.Out("[KitsuneTraderUnlock] NOTE: This only unlocks the client UI. The server must also allow edits (via KitsuneCommand's 'ktrader off' or equivalent) or your edits will be rejected.");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneTraderUnlock] Failed to apply Harmony patches: {ex.Message}");
                Log.Exception(ex);
            }
        }
    }
}
