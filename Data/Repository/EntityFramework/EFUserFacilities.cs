using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFUserFacilities : IUserFacilities
    {
        private readonly AppDbContext context;
        public EFUserFacilities(AppDbContext context)
        {
            this.context = context;
        }
        public IQueryable<UserFacility> GetUserFacilities()
        {
            return context.UserFacilities;
        }
    }
}
