using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Web.Models
{
    public class CdKeyDetailResponse
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Key { get; set; }
        public int MaxRedeemCount { get; set; }
        public string ExpiryAt { get; set; }
        public string Description { get; set; }
        public int CurrentRedeemCount { get; set; }
        public List<ItemDefinition> Items { get; set; } = new List<ItemDefinition>();
        public List<CommandDefinition> Commands { get; set; } = new List<CommandDefinition>();
    }

    public class CreateCdKeyRequest
    {
        public string Key { get; set; }
        public int MaxRedeemCount { get; set; } = 1;
        public string ExpiryAt { get; set; }
        public string Description { get; set; }
        public List<int> ItemIds { get; set; } = new List<int>();
        public List<int> CommandIds { get; set; } = new List<int>();
    }

    public class UpdateCdKeyRequest
    {
        public string Key { get; set; }
        public int MaxRedeemCount { get; set; }
        public string ExpiryAt { get; set; }
        public string Description { get; set; }
        public List<int> ItemIds { get; set; }
        public List<int> CommandIds { get; set; }
    }

    public class RedeemCdKeyRequest
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }
}
