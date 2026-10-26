using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Delivers purchases, CD keys and VIP gifts by dropping the items at the player's feet.
    ///
    /// KC used to run the <c>give</c> console command for this and ignore what it printed.
    /// <c>give</c> isn't part of the game: like <c>pm</c> it comes from TFP_CommandExtensions,
    /// so on a server without that mod (or with a copy built for another game version)
    /// every delivery failed while points were still taken and the panel said it worked.
    /// <c>give</c> also answers "Item not found." for a name the game doesn't have, and
    /// overhauls rename and remove items (a shop item set up for vanilla can be missing on
    /// Undead Legacy), so names are checked first and refused with the ones that are wrong.
    /// </summary>
    public static class GameItems
    {
        /// <summary>
        /// How long a delivery stays on the ground. The game's default, which <c>give</c> used,
        /// is 60 seconds: too short for someone who bought from the web panel while busy.
        /// </summary>
        public const float DropLifetimeSeconds = 600f;

        /// <summary>The names in <paramref name="itemNames"/> the game has no item for.</summary>
        public static List<string> Unknown(IEnumerable<string> itemNames)
            => itemNames.Where(name => !Exists(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The refusal shown to the admin or player, or null when every item exists.</summary>
        public static string UnknownItemsError(IEnumerable<string> itemNames)
        {
            var unknown = Unknown(itemNames);
            if (unknown.Count == 0) return null;
            return $"This server has no item named {string.Join(", ", unknown.Select(n => $"'{n}'"))}. " +
                   "Nothing was given or charged. Fix the item name (pick it from the item list).";
        }

        /// <summary>
        /// Drops <paramref name="items"/> at the feet of the player with this entity id. All or
        /// nothing: returns null once every item is on the ground, or why none were dropped,
        /// so callers charge or mark a gift claimed only after a null.
        /// </summary>
        public static string DropForPlayer(int entityId, IEnumerable<ItemDefinition> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return null;

            return GameThread.Run("Item delivery", () =>
            {
                var world = GameManager.Instance?.World;
                EntityPlayer player = null;
                if (world?.Players == null || !world.Players.dict.TryGetValue(entityId, out player) || player == null)
                    return "The player is no longer in the game.";

                // Build every stack before dropping any, so a bad entry drops nothing.
                var stacks = new List<ItemStack>();
                foreach (var item in list)
                {
                    var itemClass = string.IsNullOrWhiteSpace(item.ItemName) ? null : ItemClass.GetItemClass(item.ItemName, true);
                    if (itemClass == null)
                        return $"This server has no item named '{item.ItemName}'.";
                    if (item.Count <= 0)
                        return $"'{item.ItemName}' is set to a count of {item.Count}; it needs at least 1.";

                    // Default parts, as give made them, so tools and weapons get their mod slots.
                    var value = new ItemValue(itemClass.Id, true);
                    if (itemClass.HasQuality)
                        GameCompat.SetQuality(value, (ushort)Math.Max(1, Math.Min(item.Quality, 6)));
                    stacks.Add(new ItemStack(value, item.Count));
                }

                var pos = player.GetPosition();
                pos.y += 0.25f; // Slightly above feet so it doesn't clip through the ground
                foreach (var stack in stacks)
                    GameManager.Instance.ItemDropServer(stack, pos, UnityEngine.Vector3.zero, entityId, DropLifetimeSeconds, false);

                Log.Out($"[KitsuneCommand] Dropped {string.Join(", ", list.Select(i => $"{i.Count}x {i.ItemName}"))} for {player.EntityName}");
                return null;
            }, "The game didn't drop the items (see the server log).");
        }

        // Same lookup give did (ItemClass.GetItem, case-insensitive).
        private static bool Exists(string name)
            => !string.IsNullOrWhiteSpace(name) && ItemClass.GetItemClass(name, true) != null;
    }
}
