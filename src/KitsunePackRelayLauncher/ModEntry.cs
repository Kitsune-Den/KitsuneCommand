using System;
using HarmonyLib;

namespace KitsunePackRelayLauncher
{
    /// <summary>
    /// Mod entry — 7DTD calls <see cref="InitMod"/> at mod-load. We
    /// install Harmony patches and bow out.
    ///
    /// On a dedicated server, the patches install fine but the
    /// patched method (<c>XUiC_MainMenu.OnOpen</c>) only fires
    /// client-side. So the mod is safe to ship in a pack that's
    /// installed on both clients and the server -- it just no-ops
    /// on the server.
    ///
    /// The hook on XUiC_MainMenu.OnOpen reads the launcher's sentinel
    /// file and joins through the game's Direct Connect dialog -- see
    /// MainMenuOpenedPatch and DirectConnectDriver.
    /// </summary>
    public class ModEntry : IModApi
    {
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            Log.Out("[KitsunePackRelayLauncher] Initializing v0.2...");
            try
            {
                _harmony = new Harmony("net.kitsuneden.packrelaylauncher");
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                Log.Out("[KitsunePackRelayLauncher] Harmony patches applied. " +
                    "When PackRelay launcher writes packrelay-quickjoin.json " +
                    "into the userdatafolder before launching 7DTD, the next " +
                    "main-menu-ready event joins that server through Direct Connect.");
            }
            catch (Exception ex)
            {
                Log.Error("[KitsunePackRelayLauncher] Failed to apply Harmony patches: " + ex.Message);
                Log.Exception(ex);
            }
        }
    }
}
