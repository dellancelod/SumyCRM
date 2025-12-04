using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFUserCategories : IUserCategories
    {
        private readonly AppDbContext context;
        public EFUserCategories(AppDbContext context)
        {
            this.context = context;
        }
        public IQueryable<UserCategory> GetUserCategories()
        {
            return context.UserCategories;
        }
    }
}
