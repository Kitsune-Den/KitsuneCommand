using System;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;

namespace KitsunePvPBalance
{
    public class ModEntry : IModApi
    {
        public static string ModPath { get; private set; }
        private static Harmony _harmony;

        public void InitMod(Mod _modInstance)
        {
            ModPath = _modInstance.Path;
            Log.Out("[KitsunePvPBalance] Initializing...");

            LoadConfig();

            if (PvPDamageConfig.IsPatched)
            {
                Log.Out("[KitsunePvPBalance] PvP damage patch already applied (by KitsuneCommand). Skipping Harmony patching.");
                return;
            }

            try
            {
                _harmony = new Harmony("com.kitsunepvpbalance");
                _harmony.PatchAll(typeof(ModEntry).Assembly);
                PvPDamageConfig.IsPatched = true;
                Log.Out($"[KitsunePvPBalance] Harmony patches applied. PvP damage multiplier: {PvPDamageConfig.DamageMultiplier}");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsunePvPBalance] Failed to apply Harmony patches: {ex.Message}");
                Log.Exception(ex);
            }

            Log.Out($"[KitsunePvPBalance] Initialized. Enabled={PvPDamageConfig.Enabled}, " +
                    $"Multiplier={PvPDamageConfig.DamageMultiplier}, " +
                    $"Headshot={PvPDamageConfig.HeadshotMultiplier}");
        }

        public static void LoadConfig()
        {
            var configPath = Path.Combine(ModPath, "Config", "pvpbalance.json");
            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<PvPBalanceConfig>(json);
                    if (config != null)
                    {
                        PvPDamageConfig.Enabled = config.Enabled;
                        PvPDamageConfig.DamageMultiplier = Math.Max(0f, Math.Min(10f, config.DamageMultiplier));
                        PvPDamageConfig.HeadshotMultiplier = Math.Max(0f, Math.Min(10f, config.HeadshotMultiplier));
                        PvPDamageConfig.LogPvPHits = config.LogPvPHits;
                    }
                }
                else
                {
                    Log.Warning($"[KitsunePvPBalance] Config not found at {configPath}, using defaults.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsunePvPBalance] Failed to load config: {ex.Message}. Using defaults.");
            }
        }
    }

    public class PvPBalanceConfig
    {
        public bool Enabled { get; set; } = true;
        public float DamageMultiplier { get; set; } = 0.5f;
        public float HeadshotMultiplier { get; set; } = 1.0f;
        public bool LogPvPHits { get; set; } = false;
    }
}
