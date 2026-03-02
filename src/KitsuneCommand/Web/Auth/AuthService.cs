using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// Handles user authentication and first-run admin account creation.
    /// </summary>
    public class AuthService
    {
        private readonly IUserAccountRepository _userRepo;

        public AuthService(IUserAccountRepository userRepo)
        {
            _userRepo = userRepo;
        }

        /// <summary>
        /// Ensures at least one admin account exists. On first run, creates a default
        /// admin with a random password and logs it to the server console.
        /// </summary>
        public void EnsureAdminExists()
        {
            if (_userRepo.Count() > 0)
                return;

            var password = GenerateRandomPassword(16);
            var account = new UserAccount
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash(password),
                DisplayName = "Administrator",
                Role = "admin",
                IsActive = true
            };

            _userRepo.Create(account);

            Log.Out("============================================================");
            Log.Out("[KitsuneCommand] FIRST RUN - Admin account created!");
            Log.Out($"[KitsuneCommand]   Username: admin");
            Log.Out($"[KitsuneCommand]   Password: {password}");
            Log.Out("[KitsuneCommand]   Please change this password after first login.");
            Log.Out("============================================================");

            // Also write to a file for convenience
            var passwordFile = Path.Combine(
                Path.GetDirectoryName(_userRepo is UserAccountRepository repo
                    ? "." : "."),
                "FIRST_RUN_PASSWORD.txt"
            );

            try
            {
                var saveDir = Path.Combine(GameIO.GetSaveGameDir(), "KitsuneCommand");
                passwordFile = Path.Combine(saveDir, "FIRST_RUN_PASSWORD.txt");
                File.WriteAllText(passwordFile,
                    $"KitsuneCommand Admin Credentials (delete this file after reading)\n" +
                    $"Username: admin\n" +
                    $"Password: {password}\n");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Could not write password file: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates username/password and returns the user account if valid.
        /// </summary>
        public UserAccount ValidateCredentials(string username, string password)
        {
            var account = _userRepo.GetByUsername(username);
            if (account == null)
                return null;

            if (!PasswordHasher.Verify(password, account.PasswordHash))
                return null;

            _userRepo.UpdateLastLogin(username);
            return account;
        }

        /// <summary>
        /// Changes a user's password.
        /// </summary>
        public bool ChangePassword(string username, string currentPassword, string newPassword)
        {
            var account = ValidateCredentials(username, currentPassword);
            if (account == null)
                return false;

            account.PasswordHash = PasswordHasher.Hash(newPassword);
            _userRepo.Update(account);
            return true;
        }

        private static string GenerateRandomPassword(int length)
        {
            const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$";
            var random = new Random();
            var password = new char[length];
            for (int i = 0; i < length; i++)
            {
                password[i] = chars[random.Next(chars.Length)];
            }
            return new string(password);
        }
    }
}
