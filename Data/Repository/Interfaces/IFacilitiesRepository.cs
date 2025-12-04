using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface IFacilitiesRepository
    {
        IQueryable<Facility> GetFacilities();
        Task<Facility> GetFacilityByIdAsync(Guid id);
        Task SaveFacilityAsync(Facility entity);
        Task DeleteFacilityAsync(Guid id);
        Task DeleteFacilityAsync(Facility facility);
    }
}
