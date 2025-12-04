using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IUserCategories
    {
        IQueryable<UserCategory> GetUserCategories();
    }
}
