namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the goods SQLite table. Represents a purchasable item in the game store.
    /// </summary>
    public class Goods
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Maps to the item_definitions SQLite table. Reusable item template.
    /// </summary>
    public class ItemDefinition
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string ItemName { get; set; }
        public int Count { get; set; }
        public int Quality { get; set; }
        public int Durability { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Maps to the command_definitions SQLite table. Reusable command template.
    /// </summary>
    public class CommandDefinition
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Command { get; set; }
        public bool RunInMainThread { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Maps to the purchase_history SQLite table. Records each store purchase.
    /// </summary>
    public class PurchaseRecord
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int GoodsId { get; set; }
        public string GoodsName { get; set; }
        public int Price { get; set; }
    }
}
