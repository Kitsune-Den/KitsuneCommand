using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Web.Models
{
    public class GoodsDetailResponse
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public string Description { get; set; }
        public List<ItemDefinition> Items { get; set; } = new List<ItemDefinition>();
        public List<CommandDefinition> Commands { get; set; } = new List<CommandDefinition>();
    }

    public class CreateGoodsRequest
    {
        public string Name { get; set; }
        public int Price { get; set; }
        public string Description { get; set; }
        public List<int> ItemIds { get; set; } = new List<int>();
        public List<int> CommandIds { get; set; } = new List<int>();
    }

    public class UpdateGoodsRequest
    {
        public string Name { get; set; }
        public int Price { get; set; }
        public string Description { get; set; }
        public List<int> ItemIds { get; set; }
        public List<int> CommandIds { get; set; }
    }

    public class BuyGoodsRequest
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public class CreateItemDefinitionRequest
    {
        public string ItemName { get; set; }
        public int Count { get; set; } = 1;
        public int Quality { get; set; } = 1;
        public int Durability { get; set; } = 100;
        public string Description { get; set; }
    }

    public class UpdateItemDefinitionRequest
    {
        public string ItemName { get; set; }
        public int Count { get; set; }
        public int Quality { get; set; }
        public int Durability { get; set; }
        public string Description { get; set; }
    }

    public class CreateCommandDefinitionRequest
    {
        public string Command { get; set; }
        public bool RunInMainThread { get; set; }
        public string Description { get; set; }
    }

    public class UpdateCommandDefinitionRequest
    {
        public string Command { get; set; }
        public bool RunInMainThread { get; set; }
        public string Description { get; set; }
    }
}
