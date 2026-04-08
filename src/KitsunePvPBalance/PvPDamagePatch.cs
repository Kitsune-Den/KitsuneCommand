using HarmonyLib;

namespace KitsunePvPBalance
{
    /// <summary>
    /// Harmony patch that modifies PvP damage based on PvPDamageConfig settings.
    /// Targets EntityAlive.DamageEntity — the central method called when any entity takes damage.
    ///
    /// Logic:
    ///   1. Check if the entity being damaged is a player (EntityPlayer)
    ///   2. Check if the damage source is another player
    ///   3. If PvP → multiply damage by DamageMultiplier
    ///   4. If multiplier is 0 → skip damage entirely
    /// </summary>
    [HarmonyPatch(typeof(EntityAlive))]
    [HarmonyPatch("DamageEntity")]
    public static class PvPDamagePatch
    {
        /// <summary>
        /// Prefix: runs before the original DamageEntity method.
        /// Modifies _strength (damage amount) for PvP hits.
        /// Returns false to skip original method when PvP is fully disabled (multiplier = 0).
        /// </summary>
        static bool Prefix(EntityAlive __instance, DamageSource _damageSource, ref int _strength, bool _criticalHit, float _impulseScale)
        {
            if (!PvPDamageConfig.Enabled) return true; // Feature disabled, let vanilla handle it

            // Only care about player victims
            if (!(__instance is EntityPlayer victim)) return true;

            // Check if damage source is from another entity
            var attackerEntityId = _damageSource.getEntityId();
            if (attackerEntityId < 0) return true; // Environmental damage, not from entity

            // Look up the attacker entity
            var world = GameManager.Instance?.World;
            if (world == null) return true;

            var attacker = world.GetEntity(attackerEntityId) as EntityPlayer;
            if (attacker == null) return true; // Attacker is not a player (zombie, etc.)

            // Don't modify self-damage (fall damage, etc. that somehow has player as source)
            if (attacker.entityId == victim.entityId) return true;

            // This is a PvP hit — apply multiplier
            var multiplier = PvPDamageConfig.DamageMultiplier;

            if (PvPDamageConfig.LogPvPHits)
            {
                Log.Out($"[KitsunePvPBalance] PvP hit: {attacker.EntityName} -> {victim.EntityName}, " +
                        $"original={_strength}, multiplier={multiplier}, " +
                        $"result={(_strength * multiplier):F0}");
            }

            if (multiplier <= 0f)
            {
                // PvP damage fully disabled — skip the original method
                return false;
            }

            // Apply headshot multiplier for critical hits
            if (_criticalHit && PvPDamageConfig.HeadshotMultiplier != 1.0f)
            {
                multiplier *= PvPDamageConfig.HeadshotMultiplier;
            }

            _strength = (int)(_strength * multiplier);
            if (_strength < 1) _strength = 1; // Minimum 1 damage so the hit registers

            return true; // Continue to original method with modified damage
        }
    }
}
