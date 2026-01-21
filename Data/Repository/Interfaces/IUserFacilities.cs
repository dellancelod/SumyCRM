using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IUserFacilities
    {
        IQueryable<UserFacility> GetUserFacilities();
    }
}
