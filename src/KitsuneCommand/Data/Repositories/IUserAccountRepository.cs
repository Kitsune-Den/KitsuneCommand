using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IUserAccountRepository
    {
        UserAccount GetByUsername(string username);
        UserAccount GetById(int id);
        IEnumerable<UserAccount> GetAll();
        int Create(UserAccount account);
        void Update(UserAccount account);
        void UpdateLastLogin(string username);
        int Count();
    }
}
