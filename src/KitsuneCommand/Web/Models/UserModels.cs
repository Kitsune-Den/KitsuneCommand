namespace KitsuneCommand.Web.Models
{
    public class CreateUserRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; } = "viewer";
    }

    public class UpdateUserRequest
    {
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string NewPassword { get; set; }
    }

    public class UserResponse
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public string LastLoginAt { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; }
    }
}
