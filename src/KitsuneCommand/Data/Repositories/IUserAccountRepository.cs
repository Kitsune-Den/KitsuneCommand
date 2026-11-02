using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IUserAccountRepository
    {
        UserAccount GetByUsername(string username);
        UserAccount GetById(int id);
        UserAccount GetBySteamId(string steamId);
        IEnumerable<UserAccount> GetAll();
        int Create(UserAccount account);
        void Update(UserAccount account);
        void UpdateLastLogin(string username);
        void UpdatePassword(int id, string passwordHash);
        void SetSteamId(int id, string steamId);
        int Count();
        int CountActiveAdmins();
    }
}
