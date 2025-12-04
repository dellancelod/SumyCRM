using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFFacilitiesRepository : IFacilitiesRepository
    {
        private readonly AppDbContext context;
        public EFFacilitiesRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task DeleteFacilityAsync(Guid id)
        {
            var facility = await context.Facilities.FirstOrDefaultAsync(x => x.Id == id);


            context.Facilities.Remove(facility);
            await context.SaveChangesAsync();
        }

        public async Task DeleteFacilityAsync(Facility facility)
        {
            context.Facilities.Remove(facility);
            await context.SaveChangesAsync();
        }

        public IQueryable<Facility> GetFacilities()
        {
            return context.Facilities;
        }

        public async Task<Facility> GetFacilityByIdAsync(Guid id)
        {
            return await context.Facilities.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task SaveFacilityAsync(Facility entity)
        {
            if (entity.Id == default)
            {
                context.Entry(entity).State = EntityState.Added;
            }
            else
            {
                context.Entry(entity).State = EntityState.Modified;
            }
            await context.SaveChangesAsync();
        }
    }
}
