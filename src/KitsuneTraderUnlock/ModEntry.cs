using System;
using HarmonyLib;
using UnityEngine;

namespace KitsuneTraderUnlock
{
    public class ModEntry : IModApi
    {
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            Log.Out("[KitsuneTraderUnlock] Initializing...");

            // Dedicated servers run with -batchmode. On a server, patching the trader-area
            // checks to return false would override KitsuneCommand's `ktrader on/off` toggle
            // (the server would always act as if ktrader was off). KTU is a client-only mod —
            // bail out if we detect we're running on a dedicated server, so it's safe to drop
            // the mod into a shared Mods directory.
            if (Application.isBatchMode)
            {
                Log.Out("[KitsuneTraderUnlock] Detected dedicated server (batchmode). Skipping patches — this mod is client-only.");
                return;
            }

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
