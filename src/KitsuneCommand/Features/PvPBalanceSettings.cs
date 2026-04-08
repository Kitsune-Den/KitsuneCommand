namespace KitsuneCommand.Features
{
    public class PvPBalanceSettings
    {
        public bool Enabled { get; set; } = true;
        public float DamageMultiplier { get; set; } = 0.5f;
        public float HeadshotMultiplier { get; set; } = 1.0f;
        public bool LogPvPHits { get; set; } = false;
    }
}
