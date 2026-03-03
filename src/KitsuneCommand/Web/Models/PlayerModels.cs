using KitsuneCommand.Abstractions.Models;

namespace KitsuneCommand.Web.Models
{
    public class PlayerDetailInfo : PlayerInfo
    {
        public List<InventorySlot> BagItems { get; set; } = new List<InventorySlot>();
        public List<InventorySlot> BeltItems { get; set; } = new List<InventorySlot>();
        public List<PlayerSkillInfo> Skills { get; set; } = new List<PlayerSkillInfo>();
    }

    public class InventorySlot
    {
        public int SlotIndex { get; set; }
        public string ItemName { get; set; }
        public int Count { get; set; }
        public int Quality { get; set; }
        public float Durability { get; set; }
        public float MaxDurability { get; set; }
        public string IconName { get; set; }
    }

    public class PlayerSkillInfo
    {
        public string Name { get; set; }
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public bool IsLocked { get; set; }
    }

    public class KickRequest
    {
        public string Reason { get; set; }
    }

    public class BanRequest
    {
        public string Reason { get; set; }
        public int? DurationMinutes { get; set; }
    }

    public class GiveRequest
    {
        public string ItemName { get; set; }
        public int Count { get; set; } = 1;
        public int Quality { get; set; } = 1;
    }

    public class TeleportRequest
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
}
