using System;
using System.Collections.Generic;

namespace KitsuneCommand.Features.Suspicion
{
    public static class SuspicionDetectorNames
    {
        public const string Inventory = "inventory";
        public const string Movement = "movement";
        public const string Progression = "progression";
        public const string KillRate = "killrate";
    }

    public static class SuspicionSeverity
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        /// <summary>0 for low (and anything unknown), 1 medium, 2 high.</summary>
        public static int Rank(string severity)
        {
            switch ((severity ?? "").ToLowerInvariant())
            {
                case High: return 2;
                case Medium: return 1;
                default: return 0;
            }
        }
    }

    /// <summary>What a detector returns: one thing that looked wrong, not yet tied to a player record.</summary>
    public class SuspicionFlag
    {
        public string Detector { get; set; }
        public string Rule { get; set; }
        public string Severity { get; set; }
        public string Summary { get; set; }

        /// <summary>
        /// Identifies "the same thing again" for one player, e.g. "over_stack:resourceWood".
        /// Defaults to the rule when null.
        /// </summary>
        public string DedupKey { get; set; }

        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
    }

    /// <summary>The player a flag is about.</summary>
    public class SuspectPlayer
    {
        public string PlayerId { get; set; }
        public string PlatformId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
        public bool IsAdmin { get; set; }
    }

    /// <summary>One non-empty bag or belt slot.</summary>
    public class InventoryItemSample
    {
        public string ItemName { get; set; }
        public int Count { get; set; }
        public int Quality { get; set; }
    }

    /// <summary>What the game says an item is allowed to be.</summary>
    public class ItemMeta
    {
        /// <summary>EnumCreativeMode name: All, Player, Console, Dev, Test or None.</summary>
        public string CreativeMode { get; set; }

        /// <summary>Largest legal stack, after any server stack-size multiplier. 0 = unknown.</summary>
        public int StackLimit { get; set; }

        public bool HasQuality { get; set; }
        public bool IsBlock { get; set; }
    }

    /// <summary>One player's state from the 3-second position sweep.</summary>
    public class PlayerSample
    {
        public int EntityId { get; set; }
        public DateTime At { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        /// <summary>Riding a vehicle or otherwise attached to another entity.</summary>
        public bool Attached { get; set; }

        /// <summary>Y of the highest block in the player's column, or null when the chunk isn't loaded.</summary>
        public double? GroundHeight { get; set; }

        /// <summary>Player level, or null when progression isn't loaded yet.</summary>
        public int? Level { get; set; }
        public int? SkillPoints { get; set; }
    }

    /// <summary>
    /// Published by LivePlayerManager after each 3-second position sweep, with
    /// the extra per-player state the suspicion detectors need.
    /// </summary>
    public class PlayerSamplesEvent
    {
        public List<PlayerSample> Samples { get; set; } = new List<PlayerSample>();
    }
}
