using System.Collections.Generic;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Settings for the suspicion log. Every detector only records; none of
    /// them kicks or bans. Persisted as JSON under the "Suspicion" settings key.
    /// </summary>
    public class SuspicionSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Skip players with permission level 0 (server admins), who legitimately use creative/debug tools.</summary>
        public bool IgnoreAdmins { get; set; } = true;

        /// <summary>Player ids (platform or cross-platform) that are never flagged.</summary>
        // Replace, not append: otherwise saved JSON is added onto the seeded defaults.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> ExemptPlayerIds { get; set; } = new List<string>();

        /// <summary>The same player tripping the same check again inside this window isn't recorded twice.</summary>
        public int RepeatCooldownMinutes { get; set; } = 10;

        /// <summary>Rows older than this are deleted. 0 keeps everything.</summary>
        public int RetentionDays { get; set; } = 90;

        public string DiscordWebhookUrl { get; set; } = "";

        /// <summary>low, medium or high. Flags below this aren't sent to Discord (they're still logged).</summary>
        public string DiscordMinSeverity { get; set; } = "medium";

        /// <summary>At most one Discord alert per player per detector in this window.</summary>
        public int DiscordCooldownMinutes { get; set; } = 30;

        public InventoryDetectorSettings Inventory { get; set; } = new InventoryDetectorSettings();
        public MovementDetectorSettings Movement { get; set; } = new MovementDetectorSettings();
        public ProgressionDetectorSettings Progression { get; set; } = new ProgressionDetectorSettings();
        public KillRateDetectorSettings KillRate { get; set; } = new KillRateDetectorSettings();
    }

    public class InventoryDetectorSettings
    {
        public bool Enabled { get; set; } = true;
        public int ScanIntervalSeconds { get; set; } = 60;

        /// <summary>Items whose CreativeMode is Dev or Test: admin and test items, never in loot or recipes.</summary>
        public bool FlagDevItems { get; set; } = true;

        /// <summary>
        /// Items whose CreativeMode is None. Off by default: vanilla marks hundreds
        /// of lootable items None (schematics, quest notes, trophies, loot parts),
        /// and modded items that extend them inherit it.
        /// </summary>
        public bool FlagHiddenItems { get; set; } = false;

        public bool FlagOverQuality { get; set; } = true;

        /// <summary>Highest legitimate quality. 0 = use the game's own tier cap.</summary>
        public int MaxQuality { get; set; } = 0;

        public bool FlagOverStack { get; set; } = true;

        public bool FlagCountJumps { get; set; } = true;

        /// <summary>One item's total count rising by at least this much between two scans.</summary>
        public int CountJumpThreshold { get; set; } = 2000;

        /// <summary>
        /// Item names never flagged. A trailing * matches a prefix ("questReward*").
        /// Seeded with vanilla Dev-mode blocks players can pick up from POIs.
        /// </summary>
        // Replace, not append: otherwise saved JSON is added onto the seeded defaults.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AllowList { get; set; } = new List<string>
        {
            "decoCookingPot",
            "wallTorchLight",
            "candleWallLight",
        };
    }

    public class MovementDetectorSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Horizontal metres per second while on foot. Vanilla sprint with buffs stays well under this.</summary>
        public double MaxHorizontalSpeed { get; set; } = 15;

        /// <summary>Consecutive over-speed samples (3 s apart) before flagging.</summary>
        public int SpeedSamplesRequired { get; set; } = 2;

        /// <summary>Distance covered in one 3-second sample that counts as a teleport.</summary>
        public double JumpDistance { get; set; } = 80;

        public bool FlagFlying { get; set; } = true;

        /// <summary>Metres above the highest block in the player's column.</summary>
        public double FlyHeight { get; set; } = 12;

        /// <summary>Consecutive airborne samples before flagging (4 = about 12 s).</summary>
        public int FlySamplesRequired { get; set; } = 4;

        /// <summary>Seconds after a spawn, respawn or KC teleport during which movement isn't judged.</summary>
        public int GraceSeconds { get; set; } = 10;
    }

    public class ProgressionDetectorSettings
    {
        public bool Enabled { get; set; } = true;
        public int WindowMinutes { get; set; } = 10;

        /// <summary>Player levels gained inside the window.</summary>
        public int MaxLevelGain { get; set; } = 10;

        /// <summary>Unspent skill points gained beyond what the levels gained explain.</summary>
        public int MaxUnexplainedSkillPoints { get; set; } = 5;

        /// <summary>Seconds after spawn before progression is judged; the server copy loads late.</summary>
        public int WarmupSeconds { get; set; } = 60;
    }

    public class KillRateDetectorSettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxZombieKillsPerMinute { get; set; } = 60;
    }
}
