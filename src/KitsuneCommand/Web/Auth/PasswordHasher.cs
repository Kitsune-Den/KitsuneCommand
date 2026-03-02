namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// BCrypt-based password hashing. Replaces the original ServerKit's plaintext password storage.
    /// </summary>
    public static class PasswordHasher
    {
        private const int WorkFactor = 12;

        public static string Hash(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);
        }

        public static bool Verify(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }
    }
}
