using Microsoft.EntityFrameworkCore;
using OpenAI.Graders;
using SumyCRM.Models;
using SumyCRM.Data.Repository.Interfaces;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFRequestsRepository : IRequestsRepository
    {
        private readonly AppDbContext context;
        public EFRequestsRepository(AppDbContext context)
        {
            this.context = context;
        }
        public async Task DeleteRequestAsync(Guid id)
        {
            context.Requests.Remove(new Request { Id = id });
            await context.SaveChangesAsync();
        }

        public async Task DeleteRequestAsync(Request request)
        {
            context.Requests.Remove(request);
            await context.SaveChangesAsync();
        }

        public async Task<Request> GetRequestByIdAsync(Guid id)
        {
            return await context.Requests.FirstOrDefaultAsync(x => x.Id == id);
        }

        public IQueryable<Request> GetRequests()
        {
            return context.Requests;
        }

        public async Task SaveRequestAsync(Request entity)
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
