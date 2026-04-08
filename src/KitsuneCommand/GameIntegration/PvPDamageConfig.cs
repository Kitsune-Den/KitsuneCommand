namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Static configuration for PvP damage modification.
    /// Read by PvPDamagePatch (Harmony), written by PvPBalanceFeature.
    /// Shared static state allows the Harmony patch to read config without DI.
    /// </summary>
    public static class PvPDamageConfig
    {
        public static volatile bool Enabled = true;
        public static volatile float DamageMultiplier = 0.5f;
        public static volatile float HeadshotMultiplier = 1.0f;
        public static volatile bool LogPvPHits = false;

        /// <summary>
        /// Set to true after Harmony patches are applied.
        /// Prevents double-patching when standalone KitsunePvPBalance is also installed.
        /// </summary>
        public static volatile bool IsPatched = false;
    }
}
