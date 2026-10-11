using System.Collections.Generic;
using System.Linq;
using KitsuneCommand.GameIntegration;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// In-memory catalog of all game items, built from the engine's ItemClass registry at startup.
    /// </summary>
    public class GameItemCatalog
    {
        private List<GameItemInfo> _items = new List<GameItemInfo>();
        private List<string> _groups = new List<string>();

        /// <summary>
        /// Enumerate all ItemClass entries from the game engine and cache them.
        /// Must be called after GameStartDone when ItemClass data is available.
        /// </summary>
        public void Initialize()
        {
            var items = new List<GameItemInfo>();
            var groupSet = new HashSet<string>();

            int count = ItemClass.list != null ? ItemClass.list.Length : 0;
            for (int i = 0; i < count; i++)
            {
                var ic = ItemClass.list[i];
                if (ic == null) continue;

                var itemName = ic.GetItemName();
                if (string.IsNullOrEmpty(itemName)) continue;

                // Resolve localized display name (falls back to internal name)
                var displayName = itemName;
                try
                {
                    var localized = GameCompat.Localize(itemName);
                    if (!string.IsNullOrEmpty(localized))
                        displayName = localized;
                }
                catch { /* Localization may not be available; fall back silently */ }

                var groups = new List<string>();
                if (ic.Groups != null)
                {
                    foreach (var g in ic.Groups)
                    {
                        var trimmed = g.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            groups.Add(trimmed);
                            groupSet.Add(trimmed);
                        }
                    }
                }

                items.Add(new GameItemInfo
                {
                    Id = i,
                    ItemName = itemName,
                    DisplayName = displayName,
                    IconName = ic.GetIconName() ?? "",
                    HasQuality = ic.HasQuality,
                    MaxStack = ic.Stacknumber.Value,
                    Groups = groups
                });
            }

            _items = items;
            _groups = groupSet.OrderBy(g => g).ToList();
            Log.Out($"[KitsuneCommand] GameItemCatalog initialized: {_items.Count} items, {_groups.Count} groups.");
        }

        /// <summary>
        /// Paginated search with optional query and group filter.
        /// </summary>
        public PaginatedResponse<GameItemInfo> Search(string query, string group, int pageIndex, int pageSize)
        {
            var filtered = _items.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim().ToLowerInvariant();
                filtered = filtered.Where(item =>
                    item.ItemName.ToLowerInvariant().Contains(q) ||
                    item.DisplayName.ToLowerInvariant().Contains(q));
            }

            if (!string.IsNullOrWhiteSpace(group))
            {
                var g = group.Trim();
                filtered = filtered.Where(item => item.Groups.Contains(g));
            }

            var list = filtered.ToList();
            var total = list.Count;
            var paged = list.Skip(pageIndex * pageSize).Take(pageSize).ToList();

            return new PaginatedResponse<GameItemInfo>
            {
                Items = paged,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Get all distinct item groups/categories.
        /// </summary>
        public List<string> GetGroups()
        {
            return _groups;
        }

        /// <summary>
        /// Get a single item by exact name.
        /// </summary>
        public GameItemInfo GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _items.FirstOrDefault(i =>
                string.Equals(i.ItemName, name, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Lightweight autocomplete search — returns top N matches.
        /// </summary>
        public List<GameItemInfo> SearchNames(string query, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _items.Take(limit).ToList();

            var q = query.Trim().ToLowerInvariant();
            return _items
                .Where(i => i.ItemName.ToLowerInvariant().Contains(q)
                          || i.DisplayName.ToLowerInvariant().Contains(q))
                .Take(limit)
                .ToList();
        }
    }

    /// <summary>
    /// Represents a single game item from the engine's ItemClass registry.
    /// </summary>
    public class GameItemInfo
    {
        public int Id { get; set; }
        public string ItemName { get; set; }
        public string DisplayName { get; set; }
        public string IconName { get; set; }
        public bool HasQuality { get; set; }
        public int MaxStack { get; set; }
        public List<string> Groups { get; set; }
    }
}
