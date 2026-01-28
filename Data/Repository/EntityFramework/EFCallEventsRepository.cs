using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFCallEventsRepository : ICallEventsRepository
    {
        private readonly AppDbContext context;
        private readonly IWebHostEnvironment env;

        public EFCallEventsRepository(AppDbContext context, IWebHostEnvironment env)
        {
            this.context = context;
            this.env = env;
        }
        public async Task DeleteCallEventAsync(Guid id)
        {
            var callEvent = await context.CallEvents.FirstOrDefaultAsync(r => r.Id == id);
            if (callEvent == null)
                return;

            context.CallEvents.Remove(callEvent);
            await context.SaveChangesAsync();
        }

        public async Task DeleteCallEventAsync(CallEvent callEvent)
        {
            if (callEvent == null)
                return;

            context.CallEvents.Remove(callEvent);
            await context.SaveChangesAsync();
        }


        public async Task<CallEvent> GetCallEventByIdAsync(Guid id)
        {
            return await context.CallEvents.FirstOrDefaultAsync(x => x.Id == id);
        }

        public IQueryable<CallEvent> GetCallEvents()
        {
            return context.CallEvents;
        }

        public async Task SaveCallEventAsync(CallEvent entity)
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
