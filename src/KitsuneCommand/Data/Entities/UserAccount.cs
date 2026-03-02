namespace KitsuneCommand.Data.Entities
{
    public class UserAccount
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; } = "admin";
        public string LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
