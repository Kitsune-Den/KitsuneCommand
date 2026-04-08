namespace KitsunePvPBalance
{
    /// <summary>
    /// Static configuration for PvP damage modification.
    /// Shared between standalone mod and KitsuneCommand integration.
    /// Values are set at startup and can be updated at runtime.
    /// </summary>
    public static class PvPDamageConfig
    {
        public static volatile bool Enabled = true;
        public static volatile float DamageMultiplier = 0.5f;
        public static volatile float HeadshotMultiplier = 1.0f;
        public static volatile bool LogPvPHits = false;

        /// <summary>
        /// Set to true after Harmony patches are applied.
        /// Prevents double-patching when both standalone and KC are installed.
        /// </summary>
        public static volatile bool IsPatched = false;
    }
}
