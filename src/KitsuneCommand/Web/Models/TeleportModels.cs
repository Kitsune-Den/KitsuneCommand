namespace KitsuneCommand.Web.Models
{
    public class CreateCityLocationRequest
    {
        public string CityName { get; set; }
        public int PointsRequired { get; set; }
        public string Position { get; set; }
        public string ViewDirection { get; set; }
    }

    public class UpdateCityLocationRequest
    {
        public string CityName { get; set; }
        public int PointsRequired { get; set; }
        public string Position { get; set; }
        public string ViewDirection { get; set; }
    }

    public class TeleportToCityRequest
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public class TeleportToHomeRequest
    {
        public string PlayerId { get; set; }
    }
}
