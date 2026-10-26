namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Checks item names against the running game before KC hands them to <c>give</c>.
    ///
    /// <c>give</c> answers "Item not found." for a name the game doesn't have, and
    /// purchases, CD keys and VIP gifts used to ignore that answer: points were taken,
    /// the gift marked claimed, and the panel said it worked. Overhauls rename and remove
    /// items (a shop item set up for vanilla can be missing on Undead Legacy), so check
    /// first and refuse with the names that are wrong.
    /// </summary>
    public static class GameItems
    {
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

        // Same lookup give does (ItemClass.GetItem, case-insensitive).
        private static bool Exists(string name)
            => !string.IsNullOrWhiteSpace(name) && ItemClass.GetItemClass(name, true) != null;
    }
}
