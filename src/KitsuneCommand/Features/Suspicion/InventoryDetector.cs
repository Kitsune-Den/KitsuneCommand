using System;
using System.Collections.Generic;
using System.Linq;

namespace KitsuneCommand.Features.Suspicion
{
    /// <summary>
    /// Compares a player's bag and belt with what the game allows: admin/test
    /// items, quality over the cap, stacks over the stack limit, and one item's
    /// total jumping between two scans. Each (rule, item) is reported once per
    /// session; <see cref="Forget"/> on disconnect starts the player over.
    /// </summary>
    public class InventoryDetector
    {
        /// <summary>The vanilla quality cap, used when neither the settings nor the game give one.</summary>
        public const int DefaultMaxQuality = 6;

        private readonly object _lock = new object();
        private readonly Dictionary<int, Dictionary<string, int>> _lastTotals = new Dictionary<int, Dictionary<string, int>>();
        private readonly Dictionary<int, HashSet<string>> _reported = new Dictionary<int, HashSet<string>>();

        /// <param name="meta">Looks an item up by name; null when the game doesn't know it.</param>
        /// <param name="gameMaxQuality">The running game's quality cap, or 0 when it doesn't expose one.</param>
        public List<SuspicionFlag> Check(
            int entityId,
            IEnumerable<InventoryItemSample> items,
            Func<string, ItemMeta> meta,
            int gameMaxQuality,
            InventoryDetectorSettings settings)
        {
            var flags = new List<SuspicionFlag>();
            var slots = (items ?? Enumerable.Empty<InventoryItemSample>())
                .Where(i => i != null && !string.IsNullOrEmpty(i.ItemName) && i.Count > 0)
                .ToList();
            var maxQuality = settings.MaxQuality > 0 ? settings.MaxQuality
                : gameMaxQuality > 0 ? gameMaxQuality
                : DefaultMaxQuality;

            lock (_lock)
            {
                if (!_reported.TryGetValue(entityId, out var reported))
                    _reported[entityId] = reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    if (IsAllowed(slot.ItemName, settings.AllowList)) continue;
                    var m = meta?.Invoke(slot.ItemName);
                    if (m == null) continue;

                    var mode = m.CreativeMode ?? "";
                    if (settings.FlagDevItems && (Is(mode, "Dev") || Is(mode, "Test")))
                    {
                        Add(flags, reported, "creative_item", slot.ItemName,
                            m.IsBlock ? SuspicionSeverity.Medium : SuspicionSeverity.High,
                            $"{(m.IsBlock ? "Block" : "Item")} {slot.ItemName} is creative-mode {mode} only",
                            new Dictionary<string, object> { ["item"] = slot.ItemName, ["creativeMode"] = mode, ["count"] = slot.Count, ["isBlock"] = m.IsBlock });
                    }
                    else if (settings.FlagHiddenItems && Is(mode, "None"))
                    {
                        Add(flags, reported, "hidden_item", slot.ItemName, SuspicionSeverity.Low,
                            $"{slot.ItemName} is hidden from the creative menu (CreativeMode None)",
                            new Dictionary<string, object> { ["item"] = slot.ItemName, ["creativeMode"] = mode, ["count"] = slot.Count, ["isBlock"] = m.IsBlock });
                    }

                    if (settings.FlagOverQuality && m.HasQuality && slot.Quality > maxQuality)
                    {
                        Add(flags, reported, "over_quality", slot.ItemName, SuspicionSeverity.High,
                            $"{slot.ItemName} has quality {slot.Quality}; the cap is {maxQuality}",
                            new Dictionary<string, object> { ["item"] = slot.ItemName, ["quality"] = slot.Quality, ["maxQuality"] = maxQuality });
                    }

                    if (settings.FlagOverStack && m.StackLimit > 0 && slot.Count > m.StackLimit)
                    {
                        Add(flags, reported, "over_stack", slot.ItemName, SuspicionSeverity.High,
                            $"A stack of {slot.Count} {slot.ItemName}; the limit is {m.StackLimit}",
                            new Dictionary<string, object> { ["item"] = slot.ItemName, ["count"] = slot.Count, ["stackLimit"] = m.StackLimit });
                    }
                }

                // Count jumps compare whole-inventory totals per item, so moving a stack
                // from belt to bag (or splitting it) never looks like a gain.
                var totals = slots
                    .GroupBy(s => s.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.Count), StringComparer.OrdinalIgnoreCase);

                if (settings.FlagCountJumps && settings.CountJumpThreshold > 0
                    && _lastTotals.TryGetValue(entityId, out var previous))
                {
                    foreach (var kvp in totals)
                    {
                        if (IsAllowed(kvp.Key, settings.AllowList)) continue;
                        previous.TryGetValue(kvp.Key, out var before);
                        var gain = kvp.Value - before;
                        if (gain < settings.CountJumpThreshold) continue;

                        // Not session-deduped: every big jump is its own event.
                        flags.Add(new SuspicionFlag
                        {
                            Detector = SuspicionDetectorNames.Inventory,
                            Rule = "count_jump",
                            Severity = SuspicionSeverity.Low,
                            DedupKey = "count_jump:" + kvp.Key,
                            Summary = $"{kvp.Key} went from {before} to {kvp.Value} between scans",
                            Details = new Dictionary<string, object> { ["item"] = kvp.Key, ["before"] = before, ["after"] = kvp.Value, ["gain"] = gain },
                        });
                    }
                }
                _lastTotals[entityId] = totals;
            }

            return flags;
        }

        /// <summary>Drop everything known about a player (disconnect).</summary>
        public void Forget(int entityId)
        {
            lock (_lock)
            {
                _lastTotals.Remove(entityId);
                _reported.Remove(entityId);
            }
        }

        /// <summary>Exact name, case-insensitive, or a prefix when the entry ends in *.</summary>
        public static bool IsAllowed(string itemName, IEnumerable<string> allowList)
        {
            if (allowList == null || string.IsNullOrEmpty(itemName)) return false;
            foreach (var raw in allowList)
            {
                var entry = raw?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry.EndsWith("*"))
                {
                    if (itemName.StartsWith(entry.Substring(0, entry.Length - 1), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (string.Equals(itemName, entry, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Is(string mode, string expected) =>
            string.Equals(mode, expected, StringComparison.OrdinalIgnoreCase);

        private static void Add(List<SuspicionFlag> flags, HashSet<string> reported, string rule, string item,
            string severity, string summary, Dictionary<string, object> details)
        {
            var key = rule + ":" + item;
            if (!reported.Add(key)) return;
            flags.Add(new SuspicionFlag
            {
                Detector = SuspicionDetectorNames.Inventory,
                Rule = rule,
                Severity = severity,
                DedupKey = key,
                Summary = summary,
                Details = details,
            });
        }
    }
}
