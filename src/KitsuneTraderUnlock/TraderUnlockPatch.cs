using HarmonyLib;

namespace KitsuneTraderUnlock
{
    /// <summary>
    /// Harmony patches on every trader-area check in the game engine.
    /// Each patch makes the method return false (position is NOT in a trader area),
    /// which cascades through the client's UI and lets admins place/destroy blocks
    /// that would normally be locked.
    ///
    /// The server still authoritatively enforces trader protection when its own copy
    /// of these methods isn't patched (or when KitsuneCommand's `ktrader on` is active).
    /// So installing this mod on a vanilla server only unlocks your client's UI —
    /// actual edits are still rejected by the server unless the server allows them.
    /// </summary>
    public static class TraderUnlockPatch
    {
        [HarmonyPatch(typeof(World), "IsWithinTraderPlacingProtection", typeof(Vector3i))]
        public static class WorldIsWithinTraderPlacingProtection_Vec3i
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderPlacingProtection", typeof(UnityEngine.Bounds))]
        public static class WorldIsWithinTraderPlacingProtection_Bounds
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderArea", typeof(Vector3i))]
        public static class WorldIsWithinTraderArea_Vec3i
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderArea", typeof(Vector3i), typeof(Vector3i))]
        public static class WorldIsWithinTraderArea_Range
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Chunk), "IsTraderArea", typeof(int), typeof(int))]
        public static class ChunkIsTraderArea
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(TraderArea), "IsWithinProtectArea", typeof(UnityEngine.Vector3))]
        public static class TraderAreaIsWithinProtectArea
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }
    }
}
