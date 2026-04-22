using HarmonyLib;

namespace KitsuneCommand.GameIntegration.Harmony
{
    /// <summary>
    /// Harmony patches on all the trader-area check methods so that when
    /// TraderProtectionConfig.ProtectionEnabled is false, every code path in the game
    /// that asks "am I in a trader zone?" gets told "no".
    ///
    /// Covers:
    ///   - World.IsWithinTraderPlacingProtection(Vector3i)
    ///   - World.IsWithinTraderPlacingProtection(Bounds)
    ///   - World.IsWithinTraderArea(Vector3i)
    ///   - World.IsWithinTraderArea(Vector3i, Vector3i)
    ///   - Chunk.IsTraderArea(int, int)
    ///   - TraderArea.IsWithinProtectArea(Vector3)
    /// </summary>
    public static class TraderProtectionPatch
    {
        private static bool ShouldBypass()
        {
            return TraderProtectionConfig.FeatureEnabled
                && !TraderProtectionConfig.ProtectionEnabled;
        }

        private static void LogIfEnabled(string where)
        {
            if (TraderProtectionConfig.LogBypasses)
                Log.Out($"[KitsuneCommand] Trader protection bypassed: {where}");
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderPlacingProtection", typeof(Vector3i))]
        public static class WorldIsWithinTraderPlacingProtection_Vec3i
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, Vector3i _worldBlockPos)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"IsWithinTraderPlacingProtection({_worldBlockPos})");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderPlacingProtection", typeof(UnityEngine.Bounds))]
        public static class WorldIsWithinTraderPlacingProtection_Bounds
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, UnityEngine.Bounds _bounds)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"IsWithinTraderPlacingProtection(bounds@{_bounds.center})");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderArea", typeof(Vector3i))]
        public static class WorldIsWithinTraderArea_Vec3i
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, Vector3i _worldBlockPos)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"IsWithinTraderArea({_worldBlockPos})");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(World), "IsWithinTraderArea", typeof(Vector3i), typeof(Vector3i))]
        public static class WorldIsWithinTraderArea_Range
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, Vector3i _minPos, Vector3i _maxPos)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"IsWithinTraderArea({_minPos}..{_maxPos})");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Chunk), "IsTraderArea", typeof(int), typeof(int))]
        public static class ChunkIsTraderArea
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, int _x, int _z)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"Chunk.IsTraderArea({_x},{_z})");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(TraderArea), "IsWithinProtectArea", typeof(UnityEngine.Vector3))]
        public static class TraderAreaIsWithinProtectArea
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result, UnityEngine.Vector3 _pos)
            {
                if (!ShouldBypass()) return true;
                LogIfEnabled($"TraderArea.IsWithinProtectArea({_pos})");
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Server-side enforcement at the central write path.
        ///
        /// Vanilla 7D2D enforces trader protection client-side only — the client refuses
        /// to damage blocks in trader areas, and no packet is sent. But a modded client
        /// (KitsuneTraderUnlock, 0-SCore, etc.) bypasses that check and sends damage
        /// packets anyway, which the server happily applies because it trusts the client.
        ///
        /// GameManager.ChangeBlocks is the authoritative method that every block mutation
        /// funnels through: server-originated changes (SetBlocksRPC), client packets
        /// (NetPackageSetBlock.ProcessPackage → ChangeBlocks), block damage syncs, POI
        /// edits. Patching it is the most robust place to enforce trader protection
        /// regardless of what the client sends.
        ///
        /// When ProtectionEnabled=true, we filter out any change whose position is inside
        /// a trader area — the rest of the batch still applies (so multi-block changes
        /// like explosions that straddle a trader boundary only lose the protected blocks).
        /// </summary>
        [HarmonyPatch(typeof(GameManager), "ChangeBlocks")]
        public static class GameManagerChangeBlocksEnforcement
        {
            [HarmonyPrefix]
            public static bool Prefix(PlatformUserIdentifierAbs persistentPlayerId,
                                      System.Collections.Generic.List<BlockChangeInfo> _blocksToChange)
            {
                if (!TraderProtectionConfig.FeatureEnabled) return true;
                if (!TraderProtectionConfig.ProtectionEnabled) return true; // ktrader off
                if (_blocksToChange == null || _blocksToChange.Count == 0) return true;

                var world = GameManager.Instance?.World;
                if (world == null) return true;

                int removed = 0;
                for (int i = _blocksToChange.Count - 1; i >= 0; i--)
                {
                    // Our IsWithinTraderArea prefix is pass-through when ProtectionEnabled=true,
                    // so this returns the real vanilla answer.
                    if (world.IsWithinTraderArea(_blocksToChange[i].pos))
                    {
                        if (TraderProtectionConfig.LogBypasses)
                            Log.Out($"[KitsuneCommand] Rejected block change at {_blocksToChange[i].pos} (ktrader on)");
                        _blocksToChange.RemoveAt(i);
                        removed++;
                    }
                }

                // If every change was in a trader area, skip the original method entirely.
                // Otherwise let it proceed with the filtered list.
                return _blocksToChange.Count > 0;
            }
        }

        /// <summary>
        /// Filter incoming client packets BEFORE they're broadcast to other clients.
        ///
        /// NetPackageSetBlock.ProcessPackage does three things in order:
        ///   1. SetBlocksOnClients (broadcast to all OTHER clients)
        ///   2. DynamicMesh updates
        ///   3. ChangeBlocks (apply locally on server)
        ///
        /// Our GameManagerChangeBlocksEnforcement prefix handles step 3, but the
        /// broadcast in step 1 happens first — so other clients would see the damage
        /// even when the server rejects it. We filter the packet's blockChanges list
        /// via reflection in a prefix here so the broadcast uses the filtered list too.
        /// </summary>
        [HarmonyPatch(typeof(NetPackageSetBlock), "ProcessPackage", typeof(World), typeof(GameManager))]
        public static class NetPackageSetBlockFilter
        {
            private static System.Reflection.FieldInfo _blockChangesField;

            [HarmonyPrefix]
            public static void Prefix(NetPackageSetBlock __instance)
            {
                if (!TraderProtectionConfig.FeatureEnabled) return;
                if (!TraderProtectionConfig.ProtectionEnabled) return; // ktrader off

                var world = GameManager.Instance?.World;
                if (world == null) return;

                if (_blockChangesField == null)
                {
                    _blockChangesField = typeof(NetPackageSetBlock).GetField(
                        "blockChanges",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    if (_blockChangesField == null) return;
                }

                var list = _blockChangesField.GetValue(__instance) as System.Collections.Generic.List<BlockChangeInfo>;
                if (list == null || list.Count == 0) return;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (world.IsWithinTraderArea(list[i].pos))
                    {
                        if (TraderProtectionConfig.LogBypasses)
                            Log.Out($"[KitsuneCommand] Filtered client packet block change at {list[i].pos} (ktrader on)");
                        list.RemoveAt(i);
                    }
                }
            }
        }
    }
}
