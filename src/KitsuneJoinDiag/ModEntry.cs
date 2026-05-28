using System;
using HarmonyLib;

namespace KitsuneJoinDiag
{
    /// <summary>
    /// Mod entry point — 7DTD calls <see cref="InitMod"/> once at mod-load.
    /// We just install Harmony patches and bow out.
    ///
    /// On a dedicated server, the patches install fine but never fire — the
    /// client-side <c>NetworkClientLiteNetLib.OnDisconnectedFromServer</c>
    /// code path doesn't execute server-side (server peers run through
    /// <c>NetworkServerLiteNetLib</c>, a different class). So this mod is
    /// safe to ship in a pack that's installed on both clients and the
    /// server.
    /// </summary>
    public class ModEntry : IModApi
    {
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            Log.Out("[KitsuneJoinDiag] Initializing...");
            try
            {
                _harmony = new Harmony("net.kitsuneden.joindiag");
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                Log.Out("[KitsuneJoinDiag] Harmony patches applied. " +
                    "On a connection failure, the actual LiteNetLib DisconnectReason " +
                    "will be logged at ERR level for easy diagnosis.");
            }
            catch (Exception ex)
            {
                Log.Error("[KitsuneJoinDiag] Failed to apply Harmony patches: " + ex.Message);
                Log.Exception(ex);
            }
        }
    }
}
