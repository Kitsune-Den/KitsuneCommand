using HarmonyLib;

namespace KitsuneCommand.GameIntegration.Harmony
{
    /// <summary>
    /// Harmony patch that modifies PvP damage based on PvPDamageConfig settings.
    /// Targets EntityAlive.DamageEntity — the central method called when any entity takes damage.
    /// </summary>
    [HarmonyPatch(typeof(EntityAlive))]
    [HarmonyPatch("DamageEntity")]
    public static class PvPDamagePatch
    {
        static bool Prefix(EntityAlive __instance, DamageSource _damageSource, ref int _strength, bool _criticalHit, float _impulseScale)
        {
            if (!PvPDamageConfig.Enabled) return true;

            if (!(__instance is EntityPlayer victim)) return true;

            var attackerEntityId = _damageSource.getEntityId();
            if (attackerEntityId < 0) return true;

            var world = GameManager.Instance?.World;
            if (world == null) return true;

            var attacker = world.GetEntity(attackerEntityId) as EntityPlayer;
            if (attacker == null) return true;

            if (attacker.entityId == victim.entityId) return true;

            var multiplier = PvPDamageConfig.DamageMultiplier;

            if (PvPDamageConfig.LogPvPHits)
            {
                Log.Out($"[KitsuneCommand] PvP hit: {attacker.EntityName} -> {victim.EntityName}, " +
                        $"original={_strength}, multiplier={multiplier}, " +
                        $"result={(_strength * multiplier):F0}");
            }

            if (multiplier <= 0f)
                return false;

            if (_criticalHit && PvPDamageConfig.HeadshotMultiplier != 1.0f)
                multiplier *= PvPDamageConfig.HeadshotMultiplier;

            _strength = (int)(_strength * multiplier);
            if (_strength < 1) _strength = 1;

            return true;
        }
    }
}
