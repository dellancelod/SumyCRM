using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFSchedulesRepository : ISchedulesRepository
    {
        private readonly AppDbContext context;
        public EFSchedulesRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task DeleteScheduleAsync(Guid id)
        {
            var schedule = await context.Schedules.FirstOrDefaultAsync(x => x.Id == id);


            context.Schedules.Remove(schedule);
            await context.SaveChangesAsync();
        }

        public async Task DeleteScheduleAsync(Schedule schedule)
        {
            context.Schedules.Remove(schedule);
            await context.SaveChangesAsync();
        }

        public IQueryable<Schedule> GetSchedules()
        {
            return context.Schedules;
        }

        public async Task<Schedule> GetScheduleByIdAsync(Guid id)
        {
            return await context.Schedules.FirstOrDefaultAsync(x => x.Id == id);
        }
        public bool IsUniqueScheduleNumber(string number, Guid? id = null)
        {
            return !context.Schedules
                .Where(s => s.Number == number && s.Id != id)
                .Any();
        }

        public async Task SaveScheduleAsync(Schedule entity)
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
