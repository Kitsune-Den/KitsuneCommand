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
    /// v0.1 ships the scaffold + a verified-but-stubbed hook on
    /// XUiC_MainMenu.OnOpen that reads the sentinel file and logs
    /// what it would do. v0.2 adds the actual UI-click simulation
    /// against XUiC_ServerBrowserDirectConnect (Direct Connect
    /// dialog) -- see the patch class doc for details.
    /// </summary>
    public class ModEntry : IModApi
    {
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            Log.Out("[KitsunePackRelayLauncher] Initializing v0.1...");
            try
            {
                _harmony = new Harmony("net.kitsuneden.packrelaylauncher");
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                Log.Out("[KitsunePackRelayLauncher] Harmony patches applied. " +
                    "When PackRelay launcher writes packrelay-quickjoin.json " +
                    "into the userdatafolder before launching 7DTD, the next " +
                    "main-menu-ready event will pick it up. v0.1 logs only; " +
                    "v0.2 will actually drive the Direct Connect dialog.");
            }
            catch (Exception ex)
            {
                Log.Error("[KitsunePackRelayLauncher] Failed to apply Harmony patches: " + ex.Message);
                Log.Exception(ex);
            }
        }
    }
}
